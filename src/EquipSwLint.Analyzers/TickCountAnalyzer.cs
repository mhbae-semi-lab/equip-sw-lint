using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    /// <summary>ESL001: Environment.TickCount / GetTickCount used in a way that breaks at the 32-bit wrap.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TickCountAnalyzer : DiagnosticAnalyzer
    {
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rules.TickCount32.Descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(ctx =>
            {
                var p = (IPropertyReferenceOperation)ctx.Operation;
                if (p.Property.Name == "TickCount" && Sym.Is(p.Property.ContainingType, "System.Environment"))
                    Check(ctx, p, "Environment.TickCount");
            }, OperationKind.PropertyReference);
            context.RegisterOperationAction(ctx =>
            {
                // [DllImport("kernel32")] GetTickCount / [DllImport("winmm")] timeGetTime
                var inv = (IInvocationOperation)ctx.Operation;
                var m = inv.TargetMethod;
                if (m.IsExtern && (m.Name == "GetTickCount" || m.Name == "timeGetTime") && m.Parameters.Length == 0)
                    Check(ctx, inv, m.Name + "()");
            }, OperationKind.Invocation);
        }

        private static void Check(OperationAnalysisContext ctx, IOperation read, string name)
        {
            var bad = false;
            var parent = read.Parent;
            if (parent is IConversionOperation conv)
            {
                // widening to long/double etc. loses the modulo-2^32 arithmetic that makes differences wrap-safe
                var t = conv.Type?.SpecialType ?? SpecialType.None;
                bad = t == SpecialType.System_Int64 || t == SpecialType.System_UInt64 || t == SpecialType.System_Double
                      || t == SpecialType.System_Single || t == SpecialType.System_Decimal;
            }
            else if (parent is IBinaryOperation bin)
            {
                switch (bin.OperatorKind)
                {
                    case BinaryOperatorKind.Subtract:
                        bad = bin.IsChecked; // unchecked int difference is the wrap-safe idiom
                        break;
                    case BinaryOperatorKind.Equals:
                    case BinaryOperatorKind.NotEquals:
                    case BinaryOperatorKind.Add: // int deadline compared later by difference (deadline - TickCount <= 0) is wrap-safe; the comparison is what breaks
                        break;
                    default:
                        bad = true; // <, >, <=, >=, /, %, *
                        break;
                }
            }
            if (bad)
                ctx.ReportDiagnostic(Diagnostic.Create(Rules.TickCount32.Descriptor, read.Syntax.GetLocation(), name));
        }
    }
}
