using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Linq.Parsing
{
    public class IsNotOneOf : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.IsOneOf)
                   && expression.Method.DeclaringType == typeof(LinqExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var members = FindMembers.Determine(expression);

            var locator = mapping.FieldFor(members).SqlLocator;
            var values = expression.Arguments.Last().Value();

            if (members.Last().GetMemberType().GetTypeInfo().IsEnum)
            {
                return new EnumIsNotOneOfWhereFragment(values, serializer.EnumStorage, locator);
            }

            return new WhereFragment($"NOT({locator} = ANY(?))", values);
        }
    }

    public class EnumIsNotOneOfWhereFragment : IWhereFragment
    {
        private readonly object _values;
        private readonly string _locator;
        private readonly NpgsqlDbType _dbType;

        public EnumIsNotOneOfWhereFragment(object values, EnumStorage enumStorage, string locator)
        {
            var array = values.As<Array>();
            if (enumStorage == EnumStorage.AsInteger)
            {
                var numbers = new int[array.Length];

                for (int i = 0; i < array.Length; i++)
                {
                    numbers[i] = array.GetValue(i).As<int>();
                }

                _values = numbers;
                _dbType = NpgsqlDbType.Integer | NpgsqlDbType.Array;
            }
            else
            {
                var strings = new string[array.Length];

                for (int i = 0; i < array.Length; i++)
                {
                    strings[i] = array.GetValue(i).ToString();
                }

                _values = strings;
                _dbType = NpgsqlDbType.Varchar | NpgsqlDbType.Array;
            }

            _locator = locator;
        }

        public void Apply(CommandBuilder builder)
        {
            var param = builder.AddParameter(_values, _dbType);

            builder.Append("NOT(");
            builder.Append(_locator);
            builder.Append(" = ANY(:");
            builder.Append(param.ParameterName);
            builder.Append(")");
            builder.Append(")");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}