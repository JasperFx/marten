using System;
using System.Linq.Expressions;
using System.Reflection;
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

            return new JsonLocatorField("d.data", new StoreOptions(), enumStyle, casing, property);
        }

        private readonly Func<Expression, object> _parseObject = expression => expression.Value();

        public JsonLocatorField(string dataLocator, StoreOptions options, EnumStorage enumStyle, Casing casing, MemberInfo member) :
            this(dataLocator, options, enumStyle, casing, new[] { member }, null)
        {
        }

        public JsonLocatorField(string dataLocator, StoreOptions options, EnumStorage enumStyle, Casing casing, MemberInfo[] members, string pgType = null) : base(enumStyle, members)
        {
            var locator = CommandBuilder.BuildJsonStringLocator(dataLocator, members, casing);

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
                SqlLocator = $"{options?.DatabaseSchemaName ?? StoreOptions.DefaultDatabaseSchemaName}.mt_immutable_timestamp({locator})";
                SelectionLocator = $"CAST({locator} as {PgType})";
            }
            else if (TypeMappings.TimespanZTypes.Contains(MemberType))
            {
                SqlLocator = $"{options?.DatabaseSchemaName ?? StoreOptions.DefaultDatabaseSchemaName}.mt_immutable_timestamptz({locator})";
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
                    return raw != null ? Enum.GetName(MemberType, raw) : null;
                };
            }

            if (SelectionLocator.IsEmpty())
            {
                SelectionLocator = SqlLocator;
            }
        }

        [Obsolete("Use a constructor that takes StoreOptions instead. This might be removed in v4.0.")]
        public JsonLocatorField(string dataLocator, EnumStorage enumStyle, Casing casing, MemberInfo[] members) :
            this(dataLocator, null, enumStyle, casing, members, null)
        {
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
