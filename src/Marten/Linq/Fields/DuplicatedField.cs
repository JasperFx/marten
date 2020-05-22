using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using Npgsql.PostgresTypes;
using NpgsqlTypes;

namespace Marten.Linq.Fields
{
    public class DuplicatedField : Marten.Linq.Fields.Field, Marten.Linq.Fields.IField
    {
        private readonly Func<Expression, object> _parseObject = expression => expression.Value();
        private readonly bool useTimestampWithoutTimeZoneForDateTime;
        private string _columnName;

        public DuplicatedField(EnumStorage enumStorage, IField innerField,
            bool useTimestampWithoutTimeZoneForDateTime = true, bool notNull = false) : base(enumStorage, innerField.Members)
        {
            InnerField = innerField;
            NotNull = notNull;
            ColumnName = MemberName.ToTableAlias();
            this.useTimestampWithoutTimeZoneForDateTime = useTimestampWithoutTimeZoneForDateTime;

            if (FieldType.IsEnum)
            {
                if (enumStorage == EnumStorage.AsString)
                {
                    DbType = NpgsqlDbType.Varchar;
                    PgType = "varchar";

                    _parseObject = expression =>
                    {
                        var raw = expression.Value();
                        return Enum.GetName(FieldType, raw);
                    };
                }
                else
                {
                    DbType = NpgsqlDbType.Integer;
                    PgType = "integer";
                }
            }
            else if (FieldType.IsDateTime())
            {
                PgType = this.useTimestampWithoutTimeZoneForDateTime ? "timestamp without time zone" : "timestamp with time zone";
                DbType = this.useTimestampWithoutTimeZoneForDateTime ? NpgsqlDbType.Timestamp : NpgsqlDbType.TimestampTz;
            }
            else if (FieldType == typeof(DateTimeOffset) || FieldType == typeof(DateTimeOffset?))
            {
                PgType = "timestamp with time zone";
                DbType = NpgsqlDbType.TimestampTz;
            }
            else
            {
                DbType = TypeMappings.ToDbType(FieldType);
            }
        }

        public bool NotNull { get; }

        /// <summary>
        /// Used to override the assigned DbType used by Npgsql when a parameter
        /// is used in a query against this column
        /// </summary>
        public NpgsqlDbType DbType { get; set; }


        public UpsertArgument UpsertArgument => new UpsertArgument
        {
            Arg = "arg_" + ColumnName.ToLower(),
            Column = ColumnName.ToLower(),
            PostgresType = PgType,
            Members = Members,
            DbType = DbType
        };

        public string RawLocator => TypedLocator;

        public string ColumnName
        {
            get { return _columnName; }
            set
            {
                _columnName = value;
                TypedLocator = "d." + _columnName;
            }
        }

        internal IField InnerField { get;  }

        // TODO -- have this take in CommandBuilder
        public string UpdateSqlFragment()
        {
            return $"{ColumnName} = {InnerField.SelectorForDuplication(PgType)}";

            // var rawLocator = InnerField.RawLocator.Replace("d.", "");
            //
            // return FieldType == typeof(string) || PgType == "varchar" || PgType == "text"
            //     ? $"{ColumnName} = {rawLocator}"
            //     : $"{ColumnName} = CAST({rawLocator} as {PgType})";
        }

        /*
        public string UpdateSqlFragment()
        {
            if ((DbType & NpgsqlDbType.Array) == NpgsqlDbType.Array && PgType != "jsonb")
            {
                var jsonField = new JsonLocatorField("data", _storeOptions, _enumStorage, Casing.Default, Members, "jsonb");
                return $"{ColumnName} = CAST(ARRAY(SELECT jsonb_array_elements_text({jsonField.TypedLocator})) as {PgType})";
            }
            else
            {
                var jsonField = new JsonLocatorField("data", _storeOptions, _enumStorage, Casing.Default, Members, PgType);
                return $"{ColumnName} = {jsonField.TypedLocator}";
            }
        }
         */

        public object GetValueForCompiledQueryParameter(Expression valueExpression)
        {
            return _parseObject(valueExpression);
        }

        public bool ShouldUseContainmentOperator()
        {
            return false;
        }

        string IField.SelectorForDuplication(string pgType)
        {
            throw new NotSupportedException();
        }

        public string JSONBLocator { get; set; }

        public string LocatorFor(string rootTableAlias)
        {
            return $"{rootTableAlias}.{_columnName}";
        }

        public string TypedLocator { get; set; }

        public static DuplicatedField For<T>(StoreOptions options, Expression<Func<T, object>> expression, bool useTimestampWithoutTimeZoneForDateTime = true)
        {
            var inner = new DocumentMapping<T>(options).FieldFor(expression);

            // Hokey, but it's just for testing for now.
            if (inner.Members.Length > 1)
            {
                throw new NotSupportedException("Not yet supporting deep properties yet. Soon.");
            }

            return new DuplicatedField(options.EnumStorage, inner, useTimestampWithoutTimeZoneForDateTime);
        }

        // I say you don't need a ForeignKey
        public virtual TableColumn ToColumn()
        {
            return new TableColumn(ColumnName, PgType);
        }
    }
}
