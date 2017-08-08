using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Parsing
{
	/// <summary>
	/// Implement Equals for <see cref="int"/>, <see cref="long"/>, <see cref="decimal"/>, <see cref="Guid"/>, <see cref="bool"/>.
	/// </summary>
	/// <remarks>Equals(object) calls into <see cref="Convert.ChangeType(object, Type)"/>. Equals(null) is converted to "is null" query.</remarks>
	public sealed class SimpleEqualsParser : IMethodCallParser
	{
		private static readonly Type[] SupportedTypes = {
			typeof(int), typeof(long), typeof(decimal), typeof(Guid), typeof(bool)
		};

		public bool Matches(MethodCallExpression expression)
		{
			return SupportedTypes.Contains(expression.Method.DeclaringType) &&
				   expression.Method.Name.Equals("Equals", StringComparison.Ordinal);
		}

		public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
		{
			var locator = GetLocator(mapping, expression);

			var value = expression.Arguments.OfType<ConstantExpression>().FirstOrDefault();
			if (value == null) throw new BadLinqExpressionException("Could not extract value from {0}.".ToFormat(expression), null);

			object valueToQuery = value.Value;

			if (valueToQuery == null)
			{
				return new WhereFragment($"({locator}) is null");
			}

			if (valueToQuery.GetType() != expression.Method.DeclaringType)
			{
				try
				{
					valueToQuery = Convert.ChangeType(value.Value, expression.Method.DeclaringType);
				}
				catch (Exception e)
				{
					throw new BadLinqExpressionException(
						$"Could not convert {value.Value.GetType().FullName} to {expression.Method.DeclaringType}", e);
				}
			}
			
			if (mapping.PropertySearching == PropertySearching.ContainmentOperator)
			{
				var dict = new Dictionary<string, object>();
				ContainmentWhereFragment.CreateDictionaryForSearch(dict, expression, valueToQuery);
				return new ContainmentWhereFragment(serializer, dict);
			}

			return new WhereFragment($"{locator} = ?", valueToQuery);
		}

		private static string GetLocator(IQueryableDocument mapping, MethodCallExpression expression)
		{
			if (!expression.Method.IsStatic && expression.Object != null && expression.Object.NodeType != ExpressionType.Constant)
			{
				// x.member.Equals(...)
				return mapping.JsonLocator(expression.Object);
			}
			if (expression.Arguments[0].NodeType == ExpressionType.Constant)
			{
				// type.Equals("value", x.member) [decimal]
				return mapping.JsonLocator(expression.Arguments[1]);
			}
			// type.Equals(x.member, "value") [decimal]
			return mapping.JsonLocator(expression.Arguments[0]);
		}
	}
}