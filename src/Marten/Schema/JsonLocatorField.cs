using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Util;

namespace Marten.Schema
{
    [Obsolete("Going away with better Linq IField support")]
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

            var isStringEnum = FieldType.IsEnum && enumStyle == EnumStorage.AsString;

            if (!string.IsNullOrWhiteSpace(pgType))
            {
                PgType = pgType;
            }

            if (FieldType == typeof(string) || isStringEnum)
            {
                TypedLocator = $"{locator}";
            }
            else if (TypeMappings.TimespanTypes.Contains(FieldType))
            {
                TypedLocator = $"{options?.DatabaseSchemaName ?? StoreOptions.DefaultDatabaseSchemaName}.mt_immutable_timestamp({locator})";
                RawLocator = $"CAST({locator} as {PgType})";
            }
            else if (TypeMappings.TimespanZTypes.Contains(FieldType))
            {
                TypedLocator = $"{options?.DatabaseSchemaName ?? StoreOptions.DefaultDatabaseSchemaName}.mt_immutable_timestamptz({locator})";
                RawLocator = $"CAST({locator} as {PgType})";
            }
            else if (FieldType.IsArray)
            {
                TypedLocator = $"CAST({locator} as jsonb)";
            }
            else
            {
                TypedLocator = $"CAST({locator} as {PgType})";
            }

            if (isStringEnum)
            {
                _parseObject = expression =>
                {
                    var raw = expression.Value();
                    return raw != null ? Enum.GetName(FieldType, raw) : null;
                };
            }

            if (RawLocator.IsEmpty())
            {
                RawLocator = TypedLocator;
            }
        }


        public string ToComputedIndex(DbObjectName tableName)
        {
            return $"CREATE INDEX {tableName.Name}_{MemberName.ToTableAlias()} ON {tableName.QualifiedName} (({TypedLocator}));";
        }

        public string TypedLocator { get; }
        public string RawLocator { get; }
        public string ColumnName => string.Empty;

        public void WritePatch(DocumentMapping mapping, SchemaPatch patch)
        {
            throw new NotSupportedException();
        }

        public object GetValueForCompiledQueryParameter(Expression valueExpression)
        {
            return _parseObject(valueExpression);
        }

        public bool ShouldUseContainmentOperator()
        {
            return TypeMappings.ContainmentOperatorTypes.Contains(FieldType);
        }

        public string SelectorForDuplication(string pgType)
        {
            throw new NotImplementedException();
        }

        public string JSONBLocator { get; }

        public string LocatorFor(string rootTableAlias)
        {
            // Super hokey.
            return TypedLocator.Replace("d.", rootTableAlias + ".");
        }
    }
}
