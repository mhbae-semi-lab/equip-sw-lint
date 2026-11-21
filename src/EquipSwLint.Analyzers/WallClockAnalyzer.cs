using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    /// <summary>ESL002: interval or deadline computed from two wall-clock readings (DateTime.Now - start, Now &lt; deadline).</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class WallClockAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.WallClockInterval.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSymbolStartAction(start =>
            {
                var wall = new ConcurrentDictionary<ISymbol, bool>(SymbolEqualityComparer.Default);
                var candidates = new ConcurrentBag<(IOperation Op, ISymbol Other, string Prop)>();

                start.RegisterOperationAction(ctx =>
                {
                    switch (ctx.Operation)
                    {
                        case ISimpleAssignmentOperation a when IsWallClock(a.Value) && RefSymbol(a.Target) is ISymbol s:
                            wall[s] = true; break;
                        case IVariableDeclaratorOperation d when d.Initializer != null && IsWallClock(d.Initializer.Value):
                            wall[d.Symbol] = true; break;
                        case IFieldInitializerOperation f when IsWallClock(f.Value):
                            foreach (var field in f.InitializedFields) wall[field] = true;
                            break;
                        case IBinaryOperation b when IsDateType(b.LeftOperand.Type) && IsDateType(b.RightOperand.Type) && IsIntervalOrCompare(b.OperatorKind):
                            AddCandidate(b, b.LeftOperand, b.RightOperand);
                            AddCandidate(b, b.RightOperand, b.LeftOperand);
                            break;
                        case IInvocationOperation i when i.TargetMethod.Name == "Subtract" && i.Instance != null && i.Arguments.Length == 1
                                                         && IsDateType(i.Arguments[0].Value.Type):
                            AddCandidate(i, i.Instance, i.Arguments[0].Value);
                            break;
                    }
                }, OperationKind.SimpleAssignment, OperationKind.VariableDeclarator, OperationKind.FieldInitializer,
                   OperationKind.Binary, OperationKind.Invocation);

                start.RegisterSymbolEndAction(end =>
                {
                    foreach (var c in candidates)
                        if (wall.ContainsKey(c.Other))
                            end.ReportDiagnostic(Diagnostic.Create(Rules.WallClockInterval.Descriptor, c.Op.Syntax.GetLocation(), c.Prop));
                });

                void AddCandidate(IOperation op, IOperation now, IOperation other)
                {
                    var prop = DirectNow(now);
                    if (prop != null && RefSymbol(other) is ISymbol s) candidates.Add((op, s, prop));
                }
            }, SymbolKind.NamedType);
        }

        private static bool IsDateType(ITypeSymbol? t) => Sym.Is(t, "System.DateTime") || Sym.Is(t, "System.DateTimeOffset");

        private static bool IsIntervalOrCompare(BinaryOperatorKind k) =>
            k == BinaryOperatorKind.Subtract || k == BinaryOperatorKind.LessThan || k == BinaryOperatorKind.LessThanOrEqual
            || k == BinaryOperatorKind.GreaterThan || k == BinaryOperatorKind.GreaterThanOrEqual;

        /// <summary>"Now"/"UtcNow" if the operand is a direct DateTime(Offset).Now/UtcNow read.</summary>
        private static string? DirectNow(IOperation op)
        {
            op = Sym.Unwrap(op);
            return op is IPropertyReferenceOperation p && (p.Property.Name == "Now" || p.Property.Name == "UtcNow") && IsDateType(p.Property.ContainingType)
                ? p.Property.Name : null;
        }

        private static bool IsWallClock(IOperation op)
        {
            op = Sym.Unwrap(op);
            if (DirectNow(op) != null) return true;
            if (op is IInvocationOperation i && i.Instance != null && i.TargetMethod.Name.StartsWith("Add")) return IsWallClock(i.Instance);
            if (op is IBinaryOperation b && b.OperatorKind == BinaryOperatorKind.Add) return IsWallClock(b.LeftOperand);
            return false;
        }

        private static ISymbol? RefSymbol(IOperation op)
        {
            op = Sym.Unwrap(op);
            return op switch
            {
                ILocalReferenceOperation l => l.Local,
                IFieldReferenceOperation f => f.Field,
                IPropertyReferenceOperation p when p.Property.ContainingType != null && !IsDateType(p.Property.ContainingType) => p.Property,
                _ => null
            };
        }
    }
}
