using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    /// <summary>
    /// ESL007 (buffer shared by callback and UI without lock), ESL008 (unbounded queue fed by a callback),
    /// ESL010 (field initialized after receiving started). All three need to know which thread runs each method
    /// of a type, so they share one pass: background = Task.Run/Thread/ThreadPool/timer callbacks and device events,
    /// UI = UI events, overrides of UI types, Invoke/BeginInvoke bodies. Contexts flow one way through same-type calls.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class ThreadContextAnalyzer : DiagnosticAnalyzer
    {
        internal const int Bg = 1, Ui = 2;

        // ponytail: event-name heuristic for vendor SDK callbacks; an attribute/allow-list config is the upgrade path
        private static readonly Regex DeviceEvent = new Regex(
            "Grab|Frame|Image|Captur|Acquir|Received|Arrived|DataReady|Sample|Elapsed|Exposure|Trigger|Message|Packet",
            RegexOptions.Compiled);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(
            Rules.UnlockedSharedBuffer.Descriptor, Rules.UnboundedQueue.Descriptor, Rules.StartBeforeReady.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze); // Designer.cs holds the UI event wiring
            context.EnableConcurrentExecution();
            context.RegisterSymbolStartAction(start =>
            {
                var s = new TypeState((INamedTypeSymbol)start.Symbol);
                start.RegisterOperationAction(ctx => s.Visit(ctx.Operation, ctx.ContainingSymbol),
                    OperationKind.MethodReference, OperationKind.Invocation, OperationKind.FieldReference,
                    OperationKind.SimpleAssignment, OperationKind.FieldInitializer, OperationKind.EventAssignment,
                    OperationKind.ObjectCreation);
                start.RegisterOperationBlockAction(b =>
                {
                    if (b.OwningSymbol is IMethodSymbol m)
                        foreach (var root in b.OperationBlocks) s.Bodies.Add((m, root));
                });
                start.RegisterSymbolEndAction(s.Report);
            }, SymbolKind.NamedType);
        }

        /// <summary>Where a piece of code runs: a lambda classified by how it is passed, or the enclosing method.</summary>
        internal readonly struct Origin
        {
            public Origin(IMethodSymbol? method, SyntaxNode? lambda, int fixedCtx, string name)
            { Method = method; Lambda = lambda; Fixed = fixedCtx; Name = name; }
            public IMethodSymbol? Method { get; }
            public SyntaxNode? Lambda { get; }
            public int Fixed { get; }
            public string Name { get; }
            public bool Same(Origin o) => Lambda != null ? Lambda == o.Lambda : o.Lambda == null && SymbolEqualityComparer.Default.Equals(Method, o.Method);
        }

        private sealed class Access
        {
            public IFieldSymbol Field = null!; public Origin Origin; public bool Write; public bool Locked; public IOperation Op = null!;
        }

        private sealed class TypeState
        {
            private readonly INamedTypeSymbol _type;
            private readonly ConcurrentDictionary<IMethodSymbol, int> _seeds = new ConcurrentDictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
            private readonly ConcurrentBag<(Origin From, IMethodSymbol To)> _edges = new ConcurrentBag<(Origin, IMethodSymbol)>();
            private readonly ConcurrentBag<Access> _access = new ConcurrentBag<Access>();
            private readonly ConcurrentBag<(IFieldSymbol Field, Origin Origin, IOperation Op)> _producers = new ConcurrentBag<(IFieldSymbol, Origin, IOperation)>();
            private readonly ConcurrentBag<(IFieldSymbol Field, Origin Origin)> _queueChecks = new ConcurrentBag<(IFieldSymbol, Origin)>();
            private readonly ConcurrentDictionary<IFieldSymbol, bool> _bounded = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            private readonly ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<IFieldSymbol, bool>> _fieldsByMethod =
                new ConcurrentDictionary<IMethodSymbol, ConcurrentDictionary<IFieldSymbol, bool>>(SymbolEqualityComparer.Default);
            // receiver symbol (field/local) -> handlers subscribed to its events in this type
            private readonly ConcurrentBag<(ISymbol? Receiver, IMethodSymbol? Handler, IOperation? Lambda)> _subs = new ConcurrentBag<(ISymbol?, IMethodSymbol?, IOperation?)>();
            // Thread/Timer object stored in a symbol -> its delegate
            private readonly ConcurrentBag<(ISymbol Holder, IMethodSymbol? Method, IOperation? Lambda)> _delegates = new ConcurrentBag<(ISymbol, IMethodSymbol?, IOperation?)>();
            public readonly ConcurrentBag<(IMethodSymbol Method, IOperation Root)> Bodies = new ConcurrentBag<(IMethodSymbol, IOperation)>();
            private Dictionary<IMethodSymbol, int>? _ctx;

            public TypeState(INamedTypeSymbol type) { _type = type; }

            private bool Own(ISymbol? s) => s != null && SymbolEqualityComparer.Default.Equals(s.ContainingType?.OriginalDefinition, _type.OriginalDefinition);

            public void Visit(IOperation op, ISymbol containing)
            {
                switch (op)
                {
                    case IMethodReferenceOperation mr when Own(mr.Method):
                        var c = ClassifyUse(mr);
                        if (c != 0) _seeds.AddOrUpdate(mr.Method.OriginalDefinition, c, (_, old) => old | c);
                        break;

                    case IEventAssignmentOperation ea when ea.Adds:
                        var h = Sym.Unwrap(ea.HandlerValue);
                        _subs.Add((RefSymbol((ea.EventReference as IEventReferenceOperation)?.Instance), (h as IMethodReferenceOperation)?.Method.OriginalDefinition, h as IAnonymousFunctionOperation));
                        break;

                    case IObjectCreationOperation oc when (Sym.Is(oc.Type, "System.Threading.Thread") || Sym.Is(oc.Type, "System.Threading.Timer")) && oc.Arguments.Length > 0:
                        if (AssignedSymbol(oc) is ISymbol holder)
                        {
                            var d = Sym.Unwrap(oc.Arguments[0].Value);
                            _delegates.Add((holder, (d as IMethodReferenceOperation)?.Method.OriginalDefinition, d as IAnonymousFunctionOperation));
                        }
                        break;

                    case IInvocationOperation inv:
                        if (Own(inv.TargetMethod) && !inv.TargetMethod.IsExtern)
                            _edges.Add((OriginOf(inv, containing), inv.TargetMethod.OriginalDefinition));
                        var q = QueueField(inv.Instance);
                        if (q != null)
                        {
                            var n = inv.TargetMethod.Name;
                            if (n == "Enqueue" || n == "Add" || n == "TryAdd" || n == "TryWrite" || n == "WriteAsync" || n == "Push")
                                _producers.Add((q, OriginOf(inv, containing), inv));
                            else if (n.StartsWith("TryDequeue") || n == "Dequeue" || n.StartsWith("TryTake") || n == "TryRead" || n == "Clear" || n == "Count")
                                _queueChecks.Add((q, OriginOf(inv, containing)));
                        }
                        break;

                    case ISimpleAssignmentOperation sa when RefSymbol(sa.Target) is IFieldSymbol f && Own(f):
                        NoteCreation(f, sa.Value);
                        break;

                    case IFieldInitializerOperation fi:
                        foreach (var f in fi.InitializedFields) NoteCreation(f, fi.Value);
                        break;

                    case IFieldReferenceOperation fr when Own(fr.Field):
                        var origin = OriginOf(fr, containing);
                        if (origin.Method != null)
                            _fieldsByMethod.GetOrAdd(origin.Method, _ => new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default))[fr.Field.OriginalDefinition] = true;
                        if (IsQueue(fr.Field.Type) && IsCountCheck(fr))
                            _queueChecks.Add((fr.Field.OriginalDefinition, origin));
                        if (IsBuffer(fr.Field.Type) && !fr.Field.IsVolatile && !fr.Field.IsConst)
                            _access.Add(new Access
                            {
                                Field = fr.Field.OriginalDefinition, Origin = origin, Write = IsWrite(fr),
                                Locked = Sym.Ancestor<ILockOperation>(fr) != null, Op = fr
                            });
                        break;
                }
            }

            private void NoteCreation(IFieldSymbol f, IOperation value)
            {
                value = Sym.Unwrap(value);
                if (value is IObjectCreationOperation oc && oc.Constructor != null && Sym.Is(oc.Type, "System.Collections.Concurrent.BlockingCollection`1"))
                    _bounded[f.OriginalDefinition] = oc.Constructor.Parameters.Any(p => p.Name == "boundedCapacity");
                else if (value is IInvocationOperation i && Sym.Is(i.TargetMethod.ContainingType, "System.Threading.Channels.Channel"))
                    _bounded[f.OriginalDefinition] = i.TargetMethod.Name == "CreateBounded";
            }

            private int ClassifyUse(IOperation delegateValue)
            {
                var p = delegateValue.Parent;
                while (p is IDelegateCreationOperation || p is IConversionOperation) p = p.Parent;
                if (p is IEventAssignmentOperation ea) return ea.EventReference is IEventReferenceOperation er ? ClassifyEvent(er.Event) : 0;
                if (p is IArgumentOperation arg)
                {
                    var target = arg.Parent is IInvocationOperation inv ? inv.TargetMethod
                        : arg.Parent is IObjectCreationOperation oc ? oc.Constructor : null;
                    return ClassifyTarget(target);
                }
                return 0;
            }

            private static int ClassifyTarget(IMethodSymbol? m)
            {
                if (m == null) return 0;
                var t = Sym.FullName(m.ContainingType);
                var n = m.Name;
                if (t == "System.Threading.Tasks.Task" && (n == "Run" || n == ".ctor") || t.StartsWith("System.Threading.Tasks.TaskFactory")
                    || t == "System.Threading.Thread" && n == ".ctor" || t == "System.Threading.ThreadPool"
                    || t == "System.Threading.Timer" || t == "System.Threading.Tasks.Parallel")
                    return Bg;
                if ((n == "Invoke" || n == "BeginInvoke") && Sym.IsOrImplements(m.ContainingType, "System.ComponentModel.ISynchronizeInvoke")
                    || t == "System.Windows.Threading.Dispatcher" && (n == "Invoke" || n == "BeginInvoke" || n == "InvokeAsync"))
                    return Ui;
                if (n.Contains("Callback") && !t.StartsWith("System.")) return Bg; // vendor SDK: RegisterFrameCallback(...)
                return 0;
            }

            private static int ClassifyEvent(IEventSymbol ev)
            {
                var t = ev.ContainingType;
                if (Sym.IsUiType(t)) return Ui;
                if (ev.Name == "PropertyChanged" || ev.Name == "PropertyChanging" || ev.Name == "CollectionChanged" || ev.Name == "CanExecuteChanged") return 0;
                var full = Sym.FullName(t);
                if (full == "System.Timers.Timer" || full == "System.IO.Ports.SerialPort" || full == "System.IO.FileSystemWatcher"
                    || full == "System.Diagnostics.Process" || DeviceEvent.IsMatch(ev.Name))
                    return Bg;
                return 0;
            }

            private Origin OriginOf(IOperation op, ISymbol containing)
            {
                for (var p = op.Parent; p != null; p = p.Parent)
                {
                    if (p is IAnonymousFunctionOperation lam)
                    {
                        var c = ClassifyUse(lam);
                        if (c != 0) return new Origin(null, lam.Syntax, c, "lambda@" + (lam.Syntax.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
                    }
                }
                var m = (containing as IMethodSymbol)?.OriginalDefinition;
                return new Origin(m, null, 0, m?.Name ?? containing.Name);
            }

            private int CtxOf(Origin o) => o.Lambda != null ? o.Fixed : o.Method != null && _ctx!.TryGetValue(o.Method, out var c) ? c : 0;

            private void Resolve()
            {
                _ctx = new Dictionary<IMethodSymbol, int>(_seeds, SymbolEqualityComparer.Default);
                if (Sym.IsUiType(_type))
                    foreach (var m in _type.GetMembers().OfType<IMethodSymbol>())
                        if (m.MethodKind == MethodKind.Constructor || m.IsOverride && m.Name.StartsWith("On")
                            // designer-wired handler (button1_Click) whose wiring file may not be analyzed
                            || !_seeds.ContainsKey(m) && m.Parameters.Length == 2 && m.Parameters[0].Type.SpecialType == SpecialType.System_Object
                               && Sym.InheritsFrom(m.Parameters[1].Type, "System.EventArgs"))
                            _ctx[m] = (_ctx.TryGetValue(m, out var v) ? v : 0) | Ui;
                var edges = _edges.ToList();
                for (var iter = 0; iter < 6; iter++)
                {
                    var changed = false;
                    foreach (var e in edges)
                    {
                        var from = CtxOf(e.From);
                        if (from == 0) continue;
                        var old = _ctx.TryGetValue(e.To, out var v) ? v : 0;
                        if ((old | from) != old) { _ctx[e.To] = old | from; changed = true; }
                    }
                    if (!changed) break;
                }
            }

            public void Report(SymbolAnalysisContext end)
            {
                Resolve();

                // ESL007
                foreach (var g in _access.GroupBy(a => a.Field, SymbolEqualityComparer.Default))
                {
                    var list = g.ToList();
                    if (list.Any(a => a.Locked)) continue; // synchronized somewhere: inconsistent locking is V3223's job
                    var bgWrite = list.FirstOrDefault(a => a.Write && (CtxOf(a.Origin) & Bg) != 0);
                    var uiWrite = list.FirstOrDefault(a => a.Write && (CtxOf(a.Origin) & Ui) != 0);
                    Access? w = bgWrite ?? uiWrite;
                    if (w == null) continue;
                    var otherSide = (CtxOf(w.Origin) & Bg) != 0 && w == bgWrite ? Ui : Bg;
                    var other = list.FirstOrDefault(a => (CtxOf(a.Origin) & otherSide) != 0 && !a.Origin.Same(w.Origin));
                    if (other == null) continue;
                    var bgName = otherSide == Ui ? w.Origin.Name : other.Origin.Name;
                    var uiName = otherSide == Ui ? other.Origin.Name : w.Origin.Name;
                    end.ReportDiagnostic(Diagnostic.Create(Rules.UnlockedSharedBuffer.Descriptor, w.Op.Syntax.GetLocation(), w.Field.Name, bgName, uiName));
                }

                // ESL008
                var checks = _queueChecks.ToList();
                foreach (var p in _producers)
                {
                    if (_bounded.TryGetValue(p.Field, out var bounded) ? bounded : Sym.Is(p.Field.Type, "System.Collections.Concurrent.BlockingCollection`1")
                                                                               || Sym.Is(p.Field.Type, "System.Threading.Channels.Channel`1"))
                        continue; // bounded, or creation not seen for types that can be bounded
                    if ((CtxOf(p.Origin) & Bg) == 0) continue;
                    if (checks.Any(c => SymbolEqualityComparer.Default.Equals(c.Field, p.Field) && c.Origin.Same(p.Origin))) continue;
                    end.ReportDiagnostic(Diagnostic.Create(Rules.UnboundedQueue.Descriptor, p.Op.Syntax.GetLocation(), p.Field.Name, p.Origin.Name));
                }

                // ESL010
                foreach (var (method, root) in Bodies)
                    CheckStartOrder(end, root);
            }

            private void CheckStartOrder(SymbolAnalysisContext end, IOperation root)
            {
                var stmts = new List<IOperation>();
                Flatten(root, stmts);
                var reported = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
                for (var i = 0; i < stmts.Count; i++)
                {
                    var started = Started(stmts[i], out var startText, out var startedName);
                    if (started.Count == 0) continue;
                    for (var j = i + 1; j < stmts.Count; j++)
                    {
                        if (!(stmts[j] is IExpressionStatementOperation es && es.Operation is ISimpleAssignmentOperation sa)) continue;
                        if (!(RefSymbol(sa.Target) is IFieldSymbol f) || !Own(f)) continue;
                        var fd = f.OriginalDefinition;
                        if (!started.Contains(fd) || !reported.Add(fd)) continue;
                        end.ReportDiagnostic(Diagnostic.Create(Rules.StartBeforeReady.Descriptor, sa.Syntax.GetLocation(), f.Name, startText, startedName));
                    }
                }
            }

            private static void Flatten(IOperation op, List<IOperation> into)
            {
                switch (op)
                {
                    case IMethodBodyBaseOperation mb:
                        if (mb.BlockBody != null) Flatten(mb.BlockBody, into);
                        break;
                    case IBlockOperation b:
                        foreach (var s in b.Operations) Flatten(s, into);
                        break;
                    case ITryOperation t:
                        Flatten(t.Body, into);
                        break;
                    case IUsingOperation u:
                        Flatten(u.Body, into);
                        break;
                    case ILockOperation l:
                        Flatten(l.Body, into);
                        break;
                    default:
                        into.Add(op);
                        break;
                }
            }

            /// <summary>Fields used by whatever this statement starts (port events, thread/timer/task body).</summary>
            private HashSet<IFieldSymbol> Started(IOperation stmt, out string text, out string name)
            {
                var fields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
                text = name = "";
                foreach (var op in stmt.DescendantsAndSelf())
                {
                    if (op is IAnonymousFunctionOperation) break;
                    var targets = new List<(IMethodSymbol? M, IOperation? L)>();
                    if (op is IInvocationOperation inv)
                    {
                        var m = inv.TargetMethod;
                        var t = Sym.FullName(m.ContainingType);
                        var recv = inv.Instance == null ? null : RefSymbol(inv.Instance);
                        if (t == "System.Threading.Tasks.Task" && m.Name == "Run" || t.StartsWith("System.Threading.Tasks.TaskFactory") && m.Name == "StartNew"
                            || t == "System.Threading.ThreadPool" && m.Name.EndsWith("QueueUserWorkItem")
                            || m.Name.StartsWith("Begin") && (t == "System.Net.Sockets.Socket" || t == "System.Net.Sockets.TcpListener"))
                        {
                            foreach (var a in inv.Arguments)
                            {
                                var d = Sym.Unwrap(a.Value);
                                if (d is IMethodReferenceOperation mr) targets.Add((mr.Method.OriginalDefinition, null));
                                else if (d is IAnonymousFunctionOperation lam) targets.Add((null, lam));
                            }
                        }
                        else if (recv != null && (m.Name == "Start" || m.Name == "Open" || m.Name == "Connect" || m.Name.StartsWith("Start") || m.Name.StartsWith("BeginAcqui")))
                        {
                            foreach (var d in _delegates.Where(x => SymbolEqualityComparer.Default.Equals(x.Holder, recv))) targets.Add((d.Method, d.Lambda));
                            foreach (var s in _subs.Where(x => SymbolEqualityComparer.Default.Equals(x.Receiver, recv))) targets.Add((s.Handler, s.Lambda));
                        }
                        if (targets.Count > 0) text = Sym.Short(inv.Syntax, 40);
                    }
                    else if (op is IObjectCreationOperation oc && Sym.Is(oc.Type, "System.Threading.Timer"))
                    {
                        var due = oc.Arguments.FirstOrDefault(a => a.Parameter?.Name == "dueTime");
                        if (due != null && Sym.IsConstant(Sym.Unwrap(due.Value), 0))
                        {
                            var d = Sym.Unwrap(oc.Arguments[0].Value);
                            if (d is IMethodReferenceOperation mr) targets.Add((mr.Method.OriginalDefinition, null));
                            else if (d is IAnonymousFunctionOperation lam) targets.Add((null, lam));
                            text = "new Timer(…, 0, …)";
                        }
                    }
                    foreach (var (m, l) in targets)
                    {
                        if (m != null)
                        {
                            name = name.Length == 0 ? m.Name : name;
                            AddFields(m, fields, depth: 1);
                        }
                        else if (l != null)
                        {
                            name = name.Length == 0 ? "lambda" : name;
                            foreach (var fr in l.Descendants().OfType<IFieldReferenceOperation>())
                                if (Own(fr.Field)) fields.Add(fr.Field.OriginalDefinition);
                            foreach (var call in l.Descendants().OfType<IInvocationOperation>())
                                if (Own(call.TargetMethod)) AddFields(call.TargetMethod.OriginalDefinition, fields, depth: 0);
                        }
                    }
                    if (fields.Count > 0) break;
                }
                return fields;
            }

            private void AddFields(IMethodSymbol m, HashSet<IFieldSymbol> into, int depth)
            {
                if (_fieldsByMethod.TryGetValue(m, out var fs))
                    foreach (var f in fs.Keys) into.Add(f);
                if (depth <= 0) return;
                foreach (var e in _edges)
                    if (e.From.Lambda == null && SymbolEqualityComparer.Default.Equals(e.From.Method, m))
                        AddFields(e.To, into, depth - 1);
            }
        }

        private static ISymbol? RefSymbol(IOperation? op)
        {
            if (op == null) return null;
            op = Sym.Unwrap(op);
            return op switch
            {
                ILocalReferenceOperation l => l.Local,
                IFieldReferenceOperation f => f.Field.OriginalDefinition,
                IPropertyReferenceOperation p => p.Property.OriginalDefinition,
                _ => null
            };
        }

        private static ISymbol? AssignedSymbol(IOperation op)
        {
            var p = op.Parent;
            while (p is IConversionOperation) p = p.Parent;
            if (p is IVariableInitializerOperation vi && vi.Parent is IVariableDeclaratorOperation d) return d.Symbol;
            if (p is ISimpleAssignmentOperation sa) return RefSymbol(sa.Target);
            return null;
        }

        private static IFieldSymbol? QueueField(IOperation? instance)
        {
            if (instance == null) return null;
            instance = Sym.Unwrap(instance);
            if (instance is IPropertyReferenceOperation p && (p.Property.Name == "Writer" || p.Property.Name == "Reader") && p.Instance != null)
                instance = Sym.Unwrap(p.Instance);
            return instance is IFieldReferenceOperation f && IsQueue(f.Field.Type) ? f.Field.OriginalDefinition : null;
        }

        private static bool IsQueue(ITypeSymbol t) =>
            Sym.Is(t, "System.Collections.Concurrent.ConcurrentQueue`1") || Sym.Is(t, "System.Collections.Generic.Queue`1")
            || Sym.Is(t, "System.Collections.Concurrent.BlockingCollection`1") || Sym.Is(t, "System.Threading.Channels.Channel`1")
            || Sym.Is(t, "System.Collections.Concurrent.ConcurrentStack`1") || Sym.Is(t, "System.Collections.Concurrent.ConcurrentBag`1");

        private static bool IsCountCheck(IFieldReferenceOperation fr)
        {
            var p = fr.Parent;
            if (p is IPropertyReferenceOperation p1)
                return p1.Property.Name == "Count" || p1.Property.Name == "Reader" && p1.Parent is IPropertyReferenceOperation p2 && p2.Property.Name == "Count";
            return p is IArgumentOperation a && a.Parent is IInvocationOperation i && i.TargetMethod.Name == "Count";
        }

        private static bool IsBuffer(ITypeSymbol t)
        {
            if (t is IArrayTypeSymbol) return true;
            var full = Sym.FullName(t);
            return full == "System.Drawing.Bitmap" || full == "System.Drawing.Image" || full == "System.Collections.Generic.List`1"
                   || full == "System.Windows.Media.Imaging.WriteableBitmap" || t.Name == "Mat";
        }

        private static readonly HashSet<string> Mutators = new HashSet<string>
        {
            "Add", "AddRange", "Clear", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange",
            "Reverse", "Sort", "SetPixel", "WritePixels", "SetTo", "CopyFrom", "Fill"
        };

        private static bool IsWrite(IFieldReferenceOperation fr)
        {
            var p = fr.Parent;
            if (p is ISimpleAssignmentOperation sa && sa.Target == fr) return true;
            if (p is IArrayElementReferenceOperation el && el.ArrayReference == fr)
                return el.Parent is IAssignmentOperation asg && asg.Target == el || el.Parent is IIncrementOrDecrementOperation;
            while (p is IConversionOperation c && c.IsImplicit) p = p.Parent; // byte[] passed as Array
            if (p is IArgumentOperation a && a.Parameter != null)
                return a.Parameter.RefKind != RefKind.None || Regex.IsMatch(a.Parameter.Name, "^(destination|destinationArray|dst|dest)$");
            if (p is IInvocationOperation inv && inv.Instance == fr) return Mutators.Contains(inv.TargetMethod.Name);
            return false;
        }
    }
}
