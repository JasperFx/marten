using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Linq.Parsing
{
    public class IsOneOf : IMethodCallParser
    {
        public bool Matches(MethodCallExpression expression)
        {
            return expression.Method.Name == nameof(LinqExtensions.IsOneOf)
                   && expression.Method.DeclaringType.Equals(typeof (LinqExtensions));
        }

        public IWhereFragment Parse(IQueryableDocument mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var finder = new FindMembers();
            finder.Visit(expression);

            var members = finder.Members;

            var locator = mapping.FieldFor(members).SqlLocator;
            var values = expression.Arguments.Last().Value();

            if (members.Last().GetMemberType().GetTypeInfo().IsEnum)
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

        public string ToSql(NpgsqlCommand command)
        {
            var param = command.AddParameter(_values, _dbType);

            return $"{_locator} = ANY(:{param.ParameterName})";
        }

        public bool Contains(string sqlText)
        {
            return false;
        }
    }
}