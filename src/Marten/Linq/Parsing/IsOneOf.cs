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
    public class IsOneOf : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.IsOneOf)
                   && expression.Method.DeclaringType == typeof (LinqExtensions);
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var members = FindMembers.Determine(expression);

            var locator = mapping.FieldFor(members).SqlLocator;
            var values = expression.Arguments.Last().Value();

            if (members.Last().GetMemberType().IsEnum)
            {
                return new EnumIsOneOfWhereFragment(values, serializer.EnumStorage, locator);
            }

            return new WhereFragment($"{locator} = ANY(?)", values);
        }
    }

    public class EnumIsOneOfWhereFragment : IWhereFragment
    {
        private readonly object _values;
        private readonly string _locator;
        private readonly NpgsqlDbType _dbType;

        public EnumIsOneOfWhereFragment(object values, EnumStorage enumStorage, string locator)
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

            builder.Append(_locator);
            builder.Append(" = ANY(:");
            builder.Append(param.ParameterName);
            builder.Append(")");
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}