using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Util;

namespace Marten.Schema
{
    public class JsonLocatorField : Field, IField
    {
        public static JsonLocatorField For<T>(EnumStorage enumStyle, Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);


            return new JsonLocatorField(enumStyle, property);
        }

        private readonly Func<Expression, object> _parseObject = expression => expression.Value();

        public JsonLocatorField(EnumStorage enumStyle, MemberInfo member) : base(member)
        {
            var memberType = member.GetMemberType();
            var isStringEnum = memberType.IsEnum && enumStyle == EnumStorage.AsString;

            if (memberType == typeof (string) || isStringEnum)
            {
                SqlLocator = $"d.data ->> '{member.Name}'";
            }
            else if (memberType == typeof(DateTime))
            {
                SqlLocator = $"mt_immutable_timestamp(d.data ->> '{member.Name}')";
            }
            else
            {
                SqlLocator = $"CAST(d.data ->> '{member.Name}' as {PgType})";
            }

            if (isStringEnum)
            {
                _parseObject = expression =>
                {
                    var raw = expression.Value();
                    return Enum.GetName(MemberType, raw);
                };
            }
        }

        public JsonLocatorField(EnumStorage enumStyle, MemberInfo[] members) : base(members)
        {
            var locator = "d.data";


            if (members.Length == 1)
            {
                locator += $" ->> '{members.Single().Name}'";
            }
            else
            {
                for (int i = 0; i < members.Length - 1; i++)
                {
                    locator += $" -> '{members[i].Name}'";
                }

                locator += $" ->> '{members.Last().Name}'";

            }



            SqlLocator = MemberType == typeof (string) ? locator : locator.ApplyCastToLocator(enumStyle, MemberType);


            var isStringEnum = MemberType.IsEnum && enumStyle == EnumStorage.AsString;
            if (isStringEnum)
            {
                _parseObject = expression =>
                {
                    var raw = expression.Value();
                    return Enum.GetName(MemberType, raw);
                };
            }
        }

        public string ToComputedIndex(TableName tableName)
        {
            return $"CREATE INDEX {tableName.Name}_{MemberName.ToTableAlias()} ON {tableName.QualifiedName} (({SqlLocator}));";
        }

        public string SqlLocator { get; }
        public string ColumnName => String.Empty;
        public void WritePatch(DocumentMapping mapping, SchemaPatch patch)
        {
            throw new NotSupportedException();
        }

        public object GetValue(Expression valueExpression)
        {
            return _parseObject(valueExpression);
        }

        public bool ShouldUseContainmentOperator()
        {
            return MemberType.IsOneOf(typeof(DateTime), typeof(DateTime?), typeof(DateTimeOffset),
                typeof(DateTimeOffset?));
        }
    }

    
}