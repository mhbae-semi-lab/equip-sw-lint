using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    /// <summary>ESL005: blocking wait primitives called without a timeout (Task.Wait/.Result left to VSTHRD002).</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UntimedWaitAnalyzer : DiagnosticAnalyzer
    {
        private static readonly (string Type, string[] Names)[] Waits =
        {
            ("System.Threading.WaitHandle", new[] { "WaitOne", "WaitAll", "WaitAny", "SignalAndWait" }),
            ("System.Threading.Thread", new[] { "Join" }),
            ("System.Threading.Monitor", new[] { "Wait" }),
            ("System.Threading.SemaphoreSlim", new[] { "Wait" }),
            ("System.Threading.ManualResetEventSlim", new[] { "Wait" }),
            ("System.Threading.CountdownEvent", new[] { "Wait" }),
            ("System.Threading.Barrier", new[] { "SignalAndWait" }),
            ("System.Threading.SpinWait", new[] { "SpinUntil" }),
            ("System.Collections.Concurrent.BlockingCollection`1", new[] { "Take" }),
            ("System.Diagnostics.Process", new[] { "WaitForExit" }),
        };

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.UntimedWait.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(ctx =>
            {
                var inv = (IInvocationOperation)ctx.Operation;
                var m = inv.TargetMethod;
                if (!Waits.Any(w => m.Name.Length > 0 && w.Names.Contains(m.Name) && Sym.InheritsFrom(m.ContainingType, w.Type))) return;
                // waiting on a CancellationToken's handle (alone or in WaitAny) is the cancellation path itself
                if (IsTokenHandle(inv.Instance) || inv.Arguments.Any(a => Sym.Unwrap(a.Value) is IArrayCreationOperation arr
                        && arr.Initializer != null && arr.Initializer.ElementValues.Any(IsTokenHandle))) return;
                foreach (var a in inv.Arguments)
                {
                    var t = a.Parameter?.Type;
                    if (t == null) continue;
                    if (t.SpecialType == SpecialType.System_Int32 || Sym.Is(t, "System.TimeSpan")) return; // has a timeout
                    if (Sym.Is(t, "System.Threading.CancellationToken") && !a.IsImplicit && !IsNoneToken(a.Value)) return; // cancellable
                }
                ctx.ReportDiagnostic(Diagnostic.Create(Rules.UntimedWait.Descriptor, inv.Syntax.GetLocation(), m.ContainingType.Name + "." + m.Name + "()"));
            }, OperationKind.Invocation);
        }

        private static bool IsTokenHandle(IOperation? v) =>
            v != null && Sym.Unwrap(v) is IPropertyReferenceOperation p && p.Property.Name == "WaitHandle" && Sym.Is(p.Property.ContainingType, "System.Threading.CancellationToken");

        private static bool IsNoneToken(IOperation v)
        {
            v = Sym.Unwrap(v);
            return v is IDefaultValueOperation
                   || v is IPropertyReferenceOperation p && p.Property.Name == "None" && Sym.Is(p.Property.ContainingType, "System.Threading.CancellationToken");
        }
    }

    /// <summary>ESL006: settings/recipe file truncated and rewritten in place.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class NonAtomicWriteAnalyzer : DiagnosticAnalyzer
    {
        // ponytail: path heuristics (extension/identifier words); data-flow from a settings class is the upgrade path if precision needs it
        private static readonly Regex SettingsLike = new Regex(
            @"\.(xml|json|ini|cfg|conf|config|ya?ml|rcp|recipe)\b|recipe|config|setting|param|calib|option|profile|preset|teach",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TempLike = new Regex(@"tmp|temp|bak|backup|\.new\b|\.part\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.NonAtomicWrite.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationBlockStartAction(block =>
            {
                var candidates = new System.Collections.Generic.List<(IOperation Op, string Api, IOperation Path)>();
                var replaces = false;
                block.RegisterOperationAction(ctx =>
                {
                    if (ctx.Operation is IInvocationOperation inv)
                    {
                        var m = inv.TargetMethod;
                        var type = Sym.FullName(m.ContainingType);
                        if ((type == "System.IO.File" && (m.Name == "Move" || m.Name == "Replace"))
                            || (type == "System.IO.FileInfo" && (m.Name == "MoveTo" || m.Name == "Replace")))
                        {
                            lock (candidates) replaces = true;
                            return;
                        }
                        var overwrite =
                            type == "System.IO.File" && (m.Name.StartsWith("WriteAll") || m.Name == "Create" || m.Name == "OpenWrite")
                            || m.Name == "Save" && (type == "System.Xml.Linq.XDocument" || type == "System.Xml.Linq.XElement" || type == "System.Xml.XmlDocument")
                            || m.Name == "WriteXml" && (type == "System.Data.DataSet" || type == "System.Data.DataTable")
                            || m.Name == "Create" && type == "System.Xml.XmlWriter";
                        var path = PathArg(inv.Arguments);
                        if (overwrite && path != null)
                            lock (candidates) candidates.Add((inv, m.ContainingType.Name + "." + m.Name, path));
                    }
                    else if (ctx.Operation is IObjectCreationOperation oc && oc.Constructor != null)
                    {
                        var type = Sym.FullName(oc.Type);
                        var path = PathArg(oc.Arguments);
                        if (path == null) return;
                        var ok = false;
                        if (type == "System.IO.StreamWriter" || type == "System.Xml.XmlTextWriter")
                        {
                            var append = oc.Arguments.FirstOrDefault(a => a.Parameter?.Name == "append");
                            ok = append == null || Sym.IsConstant(append.Value, 0);
                        }
                        else if (type == "System.IO.FileStream")
                        {
                            var mode = oc.Arguments.FirstOrDefault(a => a.Parameter?.Name == "mode");
                            // FileMode.Create = 2, OpenOrCreate = 4, Truncate = 5
                            ok = mode != null && (Sym.IsConstant(mode.Value, 2) || Sym.IsConstant(mode.Value, 4) || Sym.IsConstant(mode.Value, 5));
                        }
                        if (ok) lock (candidates) candidates.Add((oc, "new " + oc.Type!.Name, path));
                    }
                }, OperationKind.Invocation, OperationKind.ObjectCreation);

                block.RegisterOperationBlockEndAction(end =>
                {
                    if (replaces) return; // temp file + rename in the same method
                    foreach (var c in candidates)
                    {
                        var text = PathText(c.Path);
                        if (!SettingsLike.IsMatch(text) || TempLike.IsMatch(text)) continue;
                        end.ReportDiagnostic(Diagnostic.Create(Rules.NonAtomicWrite.Descriptor, c.Op.Syntax.GetLocation(), c.Api, Sym.Short(c.Path.Syntax, 40)));
                    }
                });
            });
        }

        private static IOperation? PathArg(ImmutableArray<IArgumentOperation> args)
        {
            var a = args.FirstOrDefault(x => x.Parameter != null && x.Parameter.Type.SpecialType == SpecialType.System_String
                                              && Regex.IsMatch(x.Parameter.Name, "path|file", RegexOptions.IgnoreCase));
            return a?.Value;
        }

        private static string PathText(IOperation path)
        {
            var text = path.Syntax.ToString();
            if (path.ConstantValue.HasValue && path.ConstantValue.Value is string s) text += " " + s;
            foreach (var r in path.DescendantsAndSelf())
            {
                if (r is ILocalReferenceOperation l)
                    text += " " + l.Local.Name + " " + l.Local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax().ToString(); // var path = ... + ".rcp"
                else if (r is IFieldReferenceOperation f) text += " " + f.Field.Name + " " + (f.Field.ConstantValue as string);
                else if (r is IParameterReferenceOperation p) text += " " + p.Parameter.Name;
                else if (r is IPropertyReferenceOperation pr) text += " " + pr.Property.Name;
            }
            return text;
        }
    }

    /// <summary>ESL009: synchronous UI marshal (Control.Invoke, Dispatcher.Invoke, SendMessage) while holding a lock.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class InvokeUnderLockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.InvokeUnderLock.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(ctx =>
            {
                var inv = (IInvocationOperation)ctx.Operation;
                var m = inv.TargetMethod;
                string? name = null;
                if (m.Name == "Invoke" && Sym.IsOrImplements(m.ContainingType, "System.ComponentModel.ISynchronizeInvoke")) name = m.ContainingType.Name + ".Invoke";
                else if (m.Name == "Invoke" && Sym.Is(m.ContainingType, "System.Windows.Threading.Dispatcher")) name = "Dispatcher.Invoke";
                else if (m.Name == "Send" && Sym.InheritsFrom(m.ContainingType, "System.Threading.SynchronizationContext")) name = "SynchronizationContext.Send";
                else if (m.IsExtern && m.Name.StartsWith("SendMessage")) name = m.Name;
                if (name == null) return;
                var lck = Sym.Ancestor<ILockOperation>(inv);
                if (lck == null) return;
                ctx.ReportDiagnostic(Diagnostic.Create(Rules.InvokeUnderLock.Descriptor, inv.Syntax.GetLocation(), name, Sym.Short(lck.LockedValue.Syntax, 32)));
            }, OperationKind.Invocation);
        }
    }

    /// <summary>ESL011: wait-only polling loop (Sleep/Delay/DoEvents) whose condition has no deadline or counter.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PollWithoutDeadlineAnalyzer : DiagnosticAnalyzer
    {
        private static readonly Regex Bounded = new Regex(
            "timeout|deadline|elapsed|expire|retry|retries|tries|attempt|count|limit|cancel|stop|exit|abort|shutdown|quit|dispos|running|alive|terminat",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.PollWithoutDeadline.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(ctx =>
            {
                if (!(ctx.Operation is IWhileLoopOperation loop)) return; // Loop also covers for/foreach
                var cond = loop.Condition;
                if (cond == null || cond.ConstantValue.HasValue) return;
                if (!IsWaitOnlyBody(loop.Body)) return;
                foreach (var op in cond.DescendantsAndSelf())
                {
                    if (op is IIncrementOrDecrementOperation || op is IAssignmentOperation) return;
                    if (IsTimeType(Sym.FullName(op.Type))) return;
                    var owner = op is IInvocationOperation io ? io.TargetMethod.ContainingType : op is IPropertyReferenceOperation po ? po.Property.ContainingType : null;
                    if (owner != null && (IsTimeType(Sym.FullName(owner)) || Sym.Is(owner, "System.Environment"))) return; // Stopwatch.GetTimestamp() < target
                    var name = op switch
                    {
                        ILocalReferenceOperation l => l.Local.Name,
                        IFieldReferenceOperation f => f.Field.Name,
                        IPropertyReferenceOperation p => p.Property.Name,
                        IParameterReferenceOperation p => p.Parameter.Name,
                        IInvocationOperation i => i.TargetMethod.Name,
                        _ => null
                    };
                    if (name != null && (Bounded.IsMatch(name) || name.StartsWith("TickCount"))) return;
                }
                ctx.ReportDiagnostic(Diagnostic.Create(Rules.PollWithoutDeadline.Descriptor, loop.Syntax.GetFirstToken().GetLocation(), Sym.Short(cond.Syntax, 40)));
            }, OperationKind.Loop);
        }

        private static bool IsTimeType(string t) =>
            t == "System.Diagnostics.Stopwatch" || t == "System.DateTime" || t == "System.TimeSpan" || t == "System.DateTimeOffset" || t == "System.TimeProvider";

        private static bool IsWaitOnlyBody(IOperation body)
        {
            var stmts = body is IBlockOperation b ? b.Operations.Where(o => !(o is IEmptyOperation)).ToList() : new System.Collections.Generic.List<IOperation> { body };
            if (stmts.Count == 0) return false;
            foreach (var s in stmts)
            {
                var e = s is IExpressionStatementOperation es ? es.Operation : s;
                if (e is IAwaitOperation aw) e = aw.Operation;
                e = Sym.Unwrap(e);
                if (!(e is IInvocationOperation inv)) return false;
                var m = inv.TargetMethod;
                var t = Sym.FullName(m.ContainingType);
                var wait = t == "System.Threading.Thread" && (m.Name == "Sleep" || m.Name == "Yield" || m.Name == "SpinWait")
                           || t == "System.Threading.Tasks.Task" && (m.Name == "Delay" || m.Name == "Yield")
                           || t == "System.Windows.Forms.Application" && m.Name == "DoEvents"
                           || t == "System.Threading.SpinWait" && m.Name == "SpinOnce";
                if (!wait) return false;
            }
            return true;
        }
    }
}
