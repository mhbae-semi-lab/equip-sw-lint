using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    /// <summary>ESL003: periodic thread-pool timer whose callback blocks and has no reentrancy guard.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TimerAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.TimerReentrancy.Descriptor);

        internal sealed class Body
        {
            public bool Guard;
            public string? Blocking;
            public readonly List<IMethodSymbol> Calls = new List<IMethodSymbol>();
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolStartAction(start =>
            {
                var type = (INamedTypeSymbol)start.Symbol;
                var bodies = new ConcurrentDictionary<IMethodSymbol, Body>(SymbolEqualityComparer.Default);
                var timers = new ConcurrentBag<(IOperation Site, string Kind, IMethodSymbol? Method, Body? Lambda, string Name)>();
                var autoResetFalse = false;

                start.RegisterOperationBlockAction(b =>
                {
                    if (b.OwningSymbol is IMethodSymbol m)
                    {
                        var body = new Body();
                        foreach (var root in b.OperationBlocks) Scan(root, body, type);
                        bodies[m] = body;
                    }
                });

                start.RegisterOperationAction(ctx =>
                {
                    switch (ctx.Operation)
                    {
                        case IObjectCreationOperation oc when Sym.Is(oc.Type, "System.Threading.Timer"):
                            var period = oc.Arguments.FirstOrDefault(a => a.Parameter?.Name == "period");
                            if (period == null || IsOneShot(period.Value)) return;
                            var cb = oc.Arguments.FirstOrDefault(a => a.Parameter?.Name == "callback");
                            if (cb != null) AddTimer(oc, "System.Threading.Timer", cb.Value);
                            break;
                        case IEventAssignmentOperation ea when ea.Adds && ea.EventReference is IEventReferenceOperation er && er.Event.Name == "Elapsed"
                                                               && Sym.Is(er.Event.ContainingType, "System.Timers.Timer"):
                            AddTimer(ea, "System.Timers.Timer", ea.HandlerValue);
                            break;
                        case ISimpleAssignmentOperation sa when sa.Target is IPropertyReferenceOperation p && p.Property.Name == "AutoReset"
                                                                && Sym.Is(p.Property.ContainingType, "System.Timers.Timer") && Sym.IsConstant(sa.Value, 0):
                            autoResetFalse = true;
                            break;
                    }
                }, OperationKind.ObjectCreation, OperationKind.EventAssignment, OperationKind.SimpleAssignment);

                start.RegisterSymbolEndAction(end =>
                {
                    foreach (var t in timers)
                    {
                        if (t.Kind == "System.Timers.Timer" && autoResetFalse) continue; // ponytail: type-wide AutoReset=false, per-timer tracking if mixed timers matter
                        var main = t.Lambda ?? (t.Method != null && bodies.TryGetValue(t.Method, out var mb) ? mb : null);
                        if (main == null) continue;
                        var guard = main.Guard;
                        var blocking = main.Blocking;
                        foreach (var callee in main.Calls) // one level into same-type helpers
                            if (bodies.TryGetValue(callee, out var cbody)) { guard |= cbody.Guard; blocking ??= cbody.Blocking; }
                        if (blocking != null && !guard)
                            end.ReportDiagnostic(Diagnostic.Create(Rules.TimerReentrancy.Descriptor, t.Site.Syntax.GetLocation(), t.Kind, t.Name));
                    }
                });

                void AddTimer(IOperation site, string kind, IOperation handler)
                {
                    var h = Sym.Unwrap(handler);
                    if (h is IMethodReferenceOperation mr)
                        timers.Add((site, kind, mr.Method.OriginalDefinition, null, mr.Method.Name));
                    else if (h is IAnonymousFunctionOperation lambda)
                    {
                        var body = new Body();
                        Scan(lambda.Body, body, type);
                        timers.Add((site, kind, null, body, "lambda"));
                    }
                }
            }, SymbolKind.NamedType);
        }

        private static bool IsOneShot(IOperation period)
        {
            var v = Sym.Unwrap(period);
            if (Sym.IsConstant(v, -1) || Sym.IsConstant(v, 0)) return true;
            return v is IFieldReferenceOperation f && f.Field.Name == "InfiniteTimeSpan"
                   || v is IPropertyReferenceOperation p && p.Property.Name == "Zero";
        }

        internal static void Scan(IOperation root, Body body, INamedTypeSymbol type)
        {
            foreach (var op in root.DescendantsAndSelf())
            {
                switch (op)
                {
                    case ILockOperation _:
                        body.Guard = true; break;
                    case IAwaitOperation _:
                        body.Blocking ??= "await"; break;
                    case IPropertyReferenceOperation p when p.Property.Name == "Result" && Sym.InheritsFrom(p.Property.ContainingType, "System.Threading.Tasks.Task`1"):
                        body.Blocking ??= "Task.Result"; break;
                    case ISimpleAssignmentOperation sa when sa.Target is IPropertyReferenceOperation tp
                                                            && (tp.Property.Name == "Enabled" || tp.Property.Name == "AutoReset")
                                                            && Sym.Is(tp.Property.ContainingType, "System.Timers.Timer"):
                        body.Guard = true; break;
                    case IConditionalOperation c when c.WhenTrue.DescendantsAndSelf().Any(x => x is IReturnOperation)
                                                      && c.Condition.DescendantsAndSelf().Any(x => x is IFieldReferenceOperation f && f.Field.Type.SpecialType == SpecialType.System_Boolean):
                        body.Guard = true; break; // if (_busy) return;
                    case IInvocationOperation inv:
                        var m = inv.TargetMethod;
                        var t = Sym.FullName(m.ContainingType);
                        if (t == "System.Threading.Monitor" && m.Name == "TryEnter"
                            || t == "System.Threading.Interlocked" && (m.Name == "CompareExchange" || m.Name == "Exchange")
                            || t == "System.Threading.SemaphoreSlim" && inv.Arguments.Length > 0
                            || t == "System.Threading.Timer" && m.Name == "Change"
                            || t == "System.Timers.Timer" && m.Name == "Stop")
                            body.Guard = true;
                        else if (IsBlocking(m, t))
                            body.Blocking ??= m.ContainingType.Name + "." + m.Name;
                        if (SymbolEqualityComparer.Default.Equals(m.ContainingType, type))
                            body.Calls.Add(m.OriginalDefinition);
                        break;
                }
            }
        }

        private static bool IsBlocking(IMethodSymbol m, string type)
        {
            var ns = m.ContainingType?.ContainingNamespace?.ToDisplayString() ?? "";
            if (ns == "System.IO" && type != "System.IO.Path") return true;
            if (ns == "System.IO.Ports" || ns == "System.Net.Sockets") return true;
            switch (type)
            {
                case "System.Threading.Thread": return m.Name == "Sleep" || m.Name == "Join";
                case "System.Threading.Tasks.Task": return m.Name == "Wait" || m.Name == "WaitAll";
                case "System.Threading.Monitor": return m.Name == "Wait";
                case "System.Diagnostics.Process": return m.Name == "WaitForExit";
                case "System.Windows.Threading.Dispatcher": return m.Name == "Invoke";
            }
            if (m.Name.StartsWith("Wait") && Sym.InheritsFrom(m.ContainingType, "System.Threading.WaitHandle")) return true;
            if (m.Name == "Invoke" && Sym.IsOrImplements(m.ContainingType, "System.ComponentModel.ISynchronizeInvoke")) return true;
            return false;
        }
    }
}
