using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    /// <summary>ESL004: serial/socket read that can block forever (no timeout set in the type, or async read without a token).</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CommReadAnalyzer : DiagnosticAnalyzer
    {
        private const string Serial = "serial", Net = "net";

        private static readonly Text SerialReason = new Text(
            "SerialPort.ReadTimeout is never set (default InfiniteTimeout)", "SerialPort.ReadTimeout 을 정하지 않았다 (기본값 무한)");
        private static readonly Text NetReason = new Text(
            "no ReceiveTimeout/ReadTimeout is set (default infinite)", "ReceiveTimeout · ReadTimeout 을 정하지 않았다 (기본값 무한)");
        private static readonly Text AsyncReason = new Text(
            "read timeouts do not apply to async reads and no CancellationToken is passed",
            "비동기 읽기에는 읽기 타임아웃이 적용되지 않는데 CancellationToken 도 넘기지 않았다");

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.ReadWithoutTimeout.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolStartAction(start =>
            {
                var timeoutSet = new ConcurrentDictionary<string, bool>();
                var commSymbols = new ConcurrentDictionary<ISymbol, string>(SymbolEqualityComparer.Default); // streams/readers over a port or socket
                var reads = new ConcurrentBag<(IInvocationOperation Op, string? Family, ISymbol? Receiver, bool Async)>();

                start.RegisterOperationAction(ctx =>
                {
                    switch (ctx.Operation)
                    {
                        case ISimpleAssignmentOperation sa when sa.Target is IPropertyReferenceOperation p:
                            var name = p.Property.Name;
                            var owner = p.Property.ContainingType;
                            if (name == "ReadTimeout" && Sym.Is(owner, "System.IO.Ports.SerialPort") && !Sym.IsConstant(sa.Value, -1))
                                timeoutSet[Serial] = true;
                            else if ((name == "ReceiveTimeout" || name == "ReadTimeout") && !Sym.IsConstant(sa.Value, -1) && !Sym.IsConstant(sa.Value, 0)
                                     && (IsNetType(owner) || Sym.InheritsFrom(owner, "System.IO.Stream")))
                                timeoutSet[Net] = true;
                            else if (name == "Blocking" && Sym.Is(owner, "System.Net.Sockets.Socket") && Sym.IsConstant(sa.Value, 0))
                                timeoutSet[Net] = true;
                            if (StreamFamily(sa.Value, commSymbols) is string af && RefSymbol(sa.Target) is ISymbol at) commSymbols[at] = af;
                            break;
                        case ISimpleAssignmentOperation sa2 when StreamFamily(sa2.Value, commSymbols) is string af2 && RefSymbol(sa2.Target) is ISymbol at2:
                            commSymbols[at2] = af2; // _stream = _client.GetStream();
                            break;
                        case IObjectCreationOperation oc when oc.Arguments.Length > 0 && (Sym.InheritsFrom(oc.Type, "System.IO.TextReader") || Sym.Is(oc.Type, "System.IO.BinaryReader")):
                            var fam = StreamFamily(oc.Arguments[0].Value, commSymbols);
                            if (fam != null && AssignedSymbol(oc) is ISymbol reader) commSymbols[reader] = fam;
                            break;
                        case IVariableDeclaratorOperation d when d.Initializer != null && StreamFamily(d.Initializer.Value, commSymbols) is string df:
                            commSymbols[d.Symbol] = df; // var s = client.GetStream(); / port.BaseStream
                            break;
                        case IInvocationOperation inv:
                            var m = inv.TargetMethod;
                            if (m.Name == "SetSocketOption" || m.Name == "Poll" && Sym.Is(m.ContainingType, "System.Net.Sockets.Socket")) { timeoutSet[Net] = true; return; }
                            if (!m.Name.StartsWith("Read") && !m.Name.StartsWith("Receive") || m.Name == "ReadExisting") return;
                            // UDP is connectionless (a listener waiting for datagrams is by design); SocketAsyncEventArgs completes via callback
                            if (Sym.Is(m.ContainingType, "System.Net.Sockets.UdpClient") || m.Parameters.Any(p => Sym.Is(p.Type, "System.Net.Sockets.SocketAsyncEventArgs"))) return;
                            var isAsync = m.Name.EndsWith("Async");
                            if (isAsync && inv.Arguments.Any(a => Sym.Is(a.Parameter?.Type, "System.Threading.CancellationToken") && !a.IsImplicit && !IsNone(a.Value))) return;
                            if (Guarded(inv)) return;
                            var t = m.ContainingType;
                            string? family = Sym.Is(t, "System.IO.Ports.SerialPort") ? Serial : IsNetType(t) || Sym.InheritsFrom(t, "System.Net.Sockets.NetworkStream") ? Net : null;
                            if (family == null && !(Sym.InheritsFrom(t, "System.IO.Stream") || Sym.InheritsFrom(t, "System.IO.TextReader") || Sym.Is(t, "System.IO.BinaryReader"))) return;
                            if (family == null && inv.Instance != null && StreamFamily(inv.Instance, commSymbols) is string direct) family = direct;
                            reads.Add((inv, family, inv.Instance == null ? null : RefSymbol(inv.Instance), isAsync));
                            break;
                    }
                }, OperationKind.SimpleAssignment, OperationKind.ObjectCreation, OperationKind.Invocation, OperationKind.VariableDeclarator);

                start.RegisterSymbolEndAction(end =>
                {
                    foreach (var r in reads)
                    {
                        var family = r.Family ?? (r.Receiver != null && commSymbols.TryGetValue(r.Receiver, out var f) ? f : null);
                        if (family == null) continue;
                        Text reason;
                        if (r.Async) reason = AsyncReason;
                        else if (timeoutSet.ContainsKey(family)) continue;
                        else reason = family == Serial ? SerialReason : NetReason;
                        var m = r.Op.TargetMethod;
                        end.ReportDiagnostic(Diagnostic.Create(Rules.ReadWithoutTimeout.Descriptor, r.Op.Syntax.GetLocation(), m.ContainingType.Name + "." + m.Name, reason));
                    }
                });
            }, SymbolKind.NamedType);
        }

        private static bool IsNetType(ITypeSymbol? t) =>
            Sym.Is(t, "System.Net.Sockets.Socket") || Sym.Is(t, "System.Net.Sockets.TcpClient");

        /// <summary>serial/net if the expression is a port or socket stream (GetStream(), BaseStream, NetworkStream, known symbol).</summary>
        private static string? StreamFamily(IOperation op, ConcurrentDictionary<ISymbol, string> known)
        {
            op = Sym.Unwrap(op);
            if (Sym.InheritsFrom(op.Type, "System.Net.Sockets.NetworkStream")) return Net;
            if (op is IInvocationOperation i && i.TargetMethod.Name == "GetStream" && Sym.Is(i.TargetMethod.ContainingType, "System.Net.Sockets.TcpClient")) return Net;
            if (op is IPropertyReferenceOperation p && p.Property.Name == "BaseStream" && Sym.Is(p.Property.ContainingType, "System.IO.Ports.SerialPort")) return Serial;
            if (op is IObjectCreationOperation oc && Sym.Is(oc.Type, "System.Net.Sockets.NetworkStream")) return Net;
            return RefSymbol(op) is ISymbol s && known.TryGetValue(s, out var f) ? f : null;
        }

        private static ISymbol? RefSymbol(IOperation op)
        {
            op = Sym.Unwrap(op);
            return op switch
            {
                ILocalReferenceOperation l => l.Local,
                IFieldReferenceOperation f => f.Field,
                IPropertyReferenceOperation p => p.Property,
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

        private static bool IsNone(IOperation v)
        {
            v = Sym.Unwrap(v);
            return v is IDefaultValueOperation || v is IPropertyReferenceOperation p && p.Property.Name == "None";
        }

        /// <summary>Read only after checking that data is there (BytesToRead, Available, DataAvailable, Poll).</summary>
        private static bool Guarded(IOperation op)
        {
            for (var p = op.Parent; p != null && !(p is IAnonymousFunctionOperation); p = p.Parent)
            {
                var cond = p is IConditionalOperation c ? c.Condition : p is IWhileLoopOperation w ? w.Condition : null;
                if (cond != null && cond.DescendantsAndSelf().Any(x =>
                        x is IPropertyReferenceOperation pr && (pr.Property.Name == "BytesToRead" || pr.Property.Name == "Available" || pr.Property.Name == "DataAvailable")
                        || x is IInvocationOperation i && i.TargetMethod.Name == "Poll"))
                    return true;
            }
            return false;
        }
    }
}
