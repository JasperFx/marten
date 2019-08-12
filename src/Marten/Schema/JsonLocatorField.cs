using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Util;

namespace Marten.Schema
{
    public class JsonLocatorField: Field, IField
    {
        public static JsonLocatorField For<T>(EnumStorage enumStyle, Casing casing, Expression<Func<T, object>> expression)
        {
            var property = ReflectionHelper.GetProperty(expression);

            return new JsonLocatorField("d.data", null, enumStyle, casing, property);
        }

        private readonly Func<Expression, object> _parseObject = expression => expression.Value();

        public JsonLocatorField(string dataLocator, string databaseSchemaName, EnumStorage enumStyle, Casing casing, MemberInfo member, string pgType = null) :
            this(dataLocator, databaseSchemaName, enumStyle, casing, new[] { member }, pgType)
        {
        }

        public JsonLocatorField(string dataLocator, string databaseSchemaName, EnumStorage enumStyle, Casing casing, MemberInfo[] members, string pgType = null) : base(enumStyle, members)
        {
            var locator = new StringBuilder(dataLocator);
            var depth = 1;
            foreach (var memberInfo in members)
            {
                locator.Append(depth == members.Length ? " ->> " : " -> ");
                locator.Append($"'{memberInfo.Name.FormatCase(casing)}'");
                depth++;
            }

            var isStringEnum = MemberType.IsEnum && enumStyle == EnumStorage.AsString;

            if (!string.IsNullOrWhiteSpace(pgType))
            {
                PgType = pgType;
            }

            if (MemberType == typeof(string) || isStringEnum)
            {
                SqlLocator = $"{locator}";
            }
            else if (TypeMappings.TimespanTypes.Contains(MemberType))
            {
                SqlLocator = $"{databaseSchemaName ?? StoreOptions.DefaultDatabaseSchemaName}.mt_immutable_timestamp({locator})";
                SelectionLocator = $"CAST({locator} as {PgType})";
            }
            else if (TypeMappings.TimespanZTypes.Contains(MemberType))
            {
                SqlLocator = $"{databaseSchemaName ?? StoreOptions.DefaultDatabaseSchemaName}.mt_immutable_timestamptz({locator})";
                SelectionLocator = $"CAST({locator} as {PgType})";
            }
            else if (MemberType.IsArray)
            {
                SqlLocator = $"CAST({locator} as jsonb)";
            }
            else
            {
                SqlLocator = $"CAST({locator} as {PgType})";
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

        public string ToComputedIndex(DbObjectName tableName)
        {
            return $"CREATE INDEX {tableName.Name}_{MemberName.ToTableAlias()} ON {tableName.QualifiedName} (({SqlLocator}));";
        }

        public string SqlLocator { get; }
        public string SelectionLocator { get; }
        public string ColumnName => string.Empty;

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
            return TypeMappings.ContainmentOperatorTypes.Contains(MemberType);
        }

        public string LocatorFor(string rootTableAlias)
        {
            // Super hokey.
            return SqlLocator.Replace("d.", rootTableAlias + ".");
        }
    }
}
