using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Exceptions;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;
using Weasel.Postgresql.Tables;

namespace Marten.Linq.Fields
{
    public class DuplicatedField: IField
    {
        private readonly Func<Expression, object> _parseObject = expression => expression.Value();
        private readonly bool useTimestampWithoutTimeZoneForDateTime;
        private string _columnName;

        public DuplicatedField(EnumStorage enumStorage, IField innerField,
            bool useTimestampWithoutTimeZoneForDateTime = true, bool notNull = false)
        {
            InnerField = innerField;
            MemberName = InnerField.Members.Select(x => x.Name).Join("");
            NotNull = notNull;
            ColumnName = MemberName.ToTableAlias();
            this.useTimestampWithoutTimeZoneForDateTime = useTimestampWithoutTimeZoneForDateTime;

            PgType = PostgresqlProvider.Instance.GetDatabaseType(FieldType, enumStorage);

            if (FieldType.IsEnum)
            {
                if (enumStorage == EnumStorage.AsString)
                {
                    DbType = NpgsqlDbType.Varchar;
                    PgType = "varchar";

                    _parseObject = expression =>
                    {
                        var raw = expression.Value();
                        if (raw == null)
                        {
                            return null;
                        }
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
                PgType = this.useTimestampWithoutTimeZoneForDateTime
                    ? "timestamp without time zone"
                    : "timestamp with time zone";
                DbType = this.useTimestampWithoutTimeZoneForDateTime
                    ? NpgsqlDbType.Timestamp
                    : NpgsqlDbType.TimestampTz;
            }
            else if (FieldType == typeof(DateTimeOffset) || FieldType == typeof(DateTimeOffset?))
            {
                PgType = "timestamp with time zone";
                DbType = NpgsqlDbType.TimestampTz;
            }
            else
            {
                DbType = PostgresqlProvider.Instance.ToParameterType(FieldType);
            }
        }

        public bool NotNull { get; }

        public bool OnlyForSearching { get; set; } = false;

        /// <summary>
        ///     Used to override the assigned DbType used by Npgsql when a parameter
        ///     is used in a query against this column
        /// </summary>
        public NpgsqlDbType DbType { get; set; }


        internal UpsertArgument UpsertArgument => new UpsertArgument
        {
            Arg = "arg_" + ColumnName.ToLower(),
            Column = ColumnName.ToLower(),
            PostgresType = PgType,
            Members = Members,
            DbType = DbType
        };

        public string ColumnName
        {
            get => _columnName;
            set
            {
                _columnName = value;
                TypedLocator = "d." + _columnName;
            }
        }

        internal IField InnerField { get; }

        public string RawLocator => TypedLocator;


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

        public ISqlFragment CreateComparison(string op, ConstantExpression value, Expression memberExpression)
        {
            if (value.Value == null)
            {
                return op switch
                {
                    "=" => new IsNullFilter(this),
                    "!=" => new IsNotNullFilter(this),
                    _ => throw new BadLinqExpressionException($"Can only compare property {MemberName} by '=' or '!=' with null value")
                };
            }

            return new ComparisonFilter(this, new CommandParameter(_parseObject(value), DbType), op);
        }

        public string JSONBLocator { get; set; }
        public string LocatorForIncludedDocumentId => TypedLocator;

        public string LocatorFor(string rootTableAlias)
        {
            return $"{rootTableAlias}.{_columnName}";
        }

        public string TypedLocator { get; set; }

        public string UpdateSqlFragment()
        {
            return $"{ColumnName} = {InnerField.SelectorForDuplication(PgType)}";
        }

        public static DuplicatedField For<T>(StoreOptions options, Expression<Func<T, object>> expression,
            bool useTimestampWithoutTimeZoneForDateTime = true)
        {
            var inner = new DocumentMapping<T>(options).FieldFor(expression);

            // Hokey, but it's just for testing for now.
            if (inner.Members.Length > 1)
                throw new NotSupportedException("Not yet supporting deep properties yet. Soon.");

            return new DuplicatedField(options.EnumStorage, inner, useTimestampWithoutTimeZoneForDateTime);
        }

        // I say you don't need a ForeignKey
        public virtual TableColumn ToColumn()
        {
            return new TableColumn(ColumnName, PgType);
        }

        void ISqlFragment.Apply(CommandBuilder builder)
        {
            builder.Append(TypedLocator);
        }

        bool ISqlFragment.Contains(string sqlText)
        {
            return TypedLocator.Contains(sqlText);
        }

        public Type FieldType => InnerField.FieldType;

        public MemberInfo[] Members => InnerField.Members;
        public string MemberName { get; }

        public string PgType { get; set; } // settable so it can be overidden by users

        public string ToOrderExpression(Expression expression)
        {
            return TypedLocator;
        }
    }
}
