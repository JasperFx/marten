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
        public static JsonLocatorField For<T>(EnumStorage enumStyle, Casing casing, Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);

            return new JsonLocatorField("d.data", new StoreOptions(), enumStyle, casing, property);
        }

        private readonly Func<Expression, object> _parseObject = expression => expression.Value();

        public JsonLocatorField(string dataLocator, StoreOptions options, EnumStorage enumStyle, Casing casing, MemberInfo member) : base(member)
        {
            var memberType = member.GetMemberType();
            var memberName = member.Name.FormatCase(casing);

            var isStringEnum = memberType.IsEnum && enumStyle == EnumStorage.AsString;
            if (memberType == typeof(string) || isStringEnum)
            {
                SqlLocator = $"{dataLocator} ->> '{memberName}'";
            }
            else if (memberType == typeof(DateTime) || memberType == typeof(DateTime?))
            {
                SqlLocator = $"{options.DatabaseSchemaName}.mt_immutable_timestamp({dataLocator} ->> '{memberName}')";
                SelectionLocator = $"CAST({dataLocator} ->> '{memberName}' as {PgType})";
            }
            else if (memberType == typeof(DateTimeOffset) || memberType == typeof(DateTimeOffset?))
            {
                SqlLocator = $"{options.DatabaseSchemaName}.mt_immutable_timestamptz({dataLocator} ->> '{memberName}')";
                SelectionLocator = $"CAST({dataLocator} ->> '{memberName}' as {PgType})";
            }
            else if (memberType.IsArray)
            {
                SqlLocator = $"CAST({dataLocator} ->> '{memberName}' as jsonb)";
            }
            else
            {
                SqlLocator = $"CAST({dataLocator} ->> '{memberName}' as {PgType})";
            }

            if (isStringEnum)
            {
                _parseObject = expression =>
                {
                    var raw = expression.Value();
                    return Enum.GetName(MemberType, raw);
                };
            }

            if (SelectionLocator.IsEmpty())
            {
                SelectionLocator = SqlLocator;
            }
        }

        public JsonLocatorField(string dataLocator, EnumStorage enumStyle, Casing casing, MemberInfo[] members) : base(members)
        {
            var locator = dataLocator;

            for (int i = 0; i < members.Length - 1; i++)
            {
                locator += $" -> '{members[i].Name.FormatCase(casing)}'";
            }

            locator += $" ->> '{members.Last().Name.FormatCase(casing)}'";

            SqlLocator = MemberType == typeof(string) ? locator : locator.ApplyCastToLocator(enumStyle, MemberType);

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

        public string ToComputedIndex(DbObjectName tableName)
        {
            return $"CREATE INDEX {tableName.Name}_{MemberName.ToTableAlias()} ON {tableName.QualifiedName} (({SqlLocator}));";
        }

        public string SqlLocator { get; }
        public string SelectionLocator { get; }
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

        public string LocatorFor(string rootTableAlias)
        {
            // Super hokey.
            return SqlLocator.Replace("d.", rootTableAlias + ".");
        }
    }
}