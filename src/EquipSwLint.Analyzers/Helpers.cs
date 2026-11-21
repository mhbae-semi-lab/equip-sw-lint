using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace EquipSwLint
{
    internal static class Sym
    {
        /// <summary>Namespace-qualified metadata name, e.g. System.Collections.Concurrent.ConcurrentQueue`1.</summary>
        public static string FullName(ITypeSymbol? t)
        {
            if (t == null) return "";
            var def = t.OriginalDefinition;
            var prefix = def.ContainingType != null ? FullName(def.ContainingType) + "+"
                : def.ContainingNamespace == null || def.ContainingNamespace.IsGlobalNamespace ? "" : def.ContainingNamespace.ToDisplayString() + ".";
            return prefix + def.MetadataName;
        }

        public static bool Is(ITypeSymbol? t, string fullName) => t != null && FullName(t) == fullName;

        public static bool InheritsFrom(ITypeSymbol? t, string fullName)
        {
            for (var x = t; x != null; x = x.BaseType)
                if (FullName(x) == fullName) return true;
            return false;
        }

        public static bool IsOrImplements(ITypeSymbol? t, string fullName) =>
            t != null && (InheritsFrom(t, fullName) || t.AllInterfaces.Any(i => FullName(i) == fullName));

        public static bool IsMethod(IMethodSymbol? m, string typeFullName, params string[] names) =>
            m != null && Is(m.ContainingType, typeFullName) && (names.Length == 0 || names.Contains(m.Name));

        /// <summary>Nearest ancestor of type T, stopping at lambdas/local functions (their bodies run later, elsewhere).</summary>
        public static T? Ancestor<T>(IOperation op) where T : class, IOperation
        {
            for (var p = op.Parent; p != null; p = p.Parent)
            {
                if (p is T t) return t;
                if (p is IAnonymousFunctionOperation || p is ILocalFunctionOperation) return null;
            }
            return null;
        }

        public static bool IsUiType(ITypeSymbol? t) =>
            InheritsFrom(t, "System.Windows.Forms.Control") || InheritsFrom(t, "System.Windows.UIElement")
            || InheritsFrom(t, "System.Windows.FrameworkContentElement") || InheritsFrom(t, "System.Windows.Application")
            || InheritsFrom(t, "System.Windows.Forms.Timer") || Is(t, "System.Windows.Threading.DispatcherTimer");

        public static string Short(SyntaxNode node, int max = 48)
        {
            var s = Regex.Replace(node.ToString(), @"\s+", " ");
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        /// <summary>Unwrap implicit conversions and delegate creations around an argument value.</summary>
        public static IOperation Unwrap(IOperation op)
        {
            while (true)
            {
                if (op is IConversionOperation c && c.IsImplicit) op = c.Operand;
                else if (op is IDelegateCreationOperation d) op = d.Target;
                else if (op is IParenthesizedOperation p) op = p.Operand;
                else return op;
            }
        }

        public static bool IsConstant(IOperation? op, long value) =>
            op != null && op.ConstantValue.HasValue && op.ConstantValue.Value is IConvertible cv
            && !(op.ConstantValue.Value is string) && Convert.ToInt64(cv) == value;
    }
}
