using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
    public abstract class StringComparisonParser: IMethodCallParser
    {
        private readonly MethodInfo[] _supportedMethods;

        public StringComparisonParser(params MethodInfo[] supportedMethods)
        {
            _supportedMethods = supportedMethods;
        }

        public bool Matches(MethodCallExpression expression)
        {
            return _supportedMethods.Any(m => AreMethodsEqual(m, expression.Method));
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var locator = GetLocator(mapping, expression);

            var value = expression.Arguments.OfType<ConstantExpression>().FirstOrDefault();
            if (value == null)
                throw new BadLinqExpressionException("Could not extract string value from {0}.".ToFormat(expression), null);

            var stringOperator = GetOperator(expression);
            return new WhereFragment("{0} {1} ?".ToFormat(locator, stringOperator), FormatValue(expression.Method, value.Value as string));
        }

        protected bool AreMethodsEqual(MethodInfo method1, MethodInfo method2)
        {
            return method1.DeclaringType == method2.DeclaringType && method1.Name == method2.Name
                && method1.GetParameters().Select(p => p.ParameterType).SequenceEqual(method2.GetParameters().Select(p => p.ParameterType));
        }

        /// <summary>
        ///     Formats the string value as appropriate for the comparison.
        /// </summary>
        /// <param name="method"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public abstract string FormatValue(MethodInfo method, string value);

        protected virtual bool IsCaseInsensitiveComparison(MethodCallExpression expression)
        {
            var comparison = expression.Arguments.OfType<ConstantExpression>().Where(a => a.Type == typeof(StringComparison)).Select(c => (StringComparison)c.Value).FirstOrDefault();

            var ignoreCaseComparisons = new[] { StringComparison.CurrentCultureIgnoreCase,
                StringComparison.InvariantCultureIgnoreCase,
                StringComparison.OrdinalIgnoreCase };
            if (ignoreCaseComparisons.Contains(comparison))
                return true;

            return false;
        }

        /// <summary>
        ///     Returns the operator to emit (e.g. LIKE/ILIKE).
        /// </summary>
        /// <param name="expression"></param>
        /// <returns></returns>
        protected virtual string GetOperator(MethodCallExpression expression)
        {
            return IsCaseInsensitiveComparison(expression) ? "ILIKE" : "LIKE";
        }

        /// <summary>
        ///     Returns a locator for the member being queried upon
        /// </summary>
        /// <param name="mapping"></param>
        /// <param name="expression"></param>
        /// <returns></returns>
        protected string GetLocator(IQueryableDocument mapping, MethodCallExpression expression)
        {
            var memberExpression = determineStringField(expression);
            return mapping.RawLocator(memberExpression);
        }

        private static Expression determineStringField(MethodCallExpression expression)
        {
            if (!expression.Method.IsStatic && expression.Object != null && expression.Object.NodeType != ExpressionType.Constant)
            {
                // x.member.Equals(...)
                return expression.Object;
            }

            if (expression.Arguments[0].NodeType == ExpressionType.Constant)
            {
                // string.Equals("value", x.member)
                return expression.Arguments[1];
            }

            // string.Equals(x.member, "value")
            return expression.Arguments[0];
        }
    }
}
