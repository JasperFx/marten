using System;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Schema
{
    public class DuplicatedField : Field, IField
    {
        private readonly NpgsqlDbType _dbType;
        private readonly EnumStorage _enumStorage;
        private readonly Func<Expression, object> _parseObject = expression => expression.Value();
        private string _columnName;

        public DuplicatedField(EnumStorage enumStorage, MemberInfo[] memberPath) : base(memberPath)
        {
            _enumStorage = enumStorage;

            ColumnName = MemberName.ToTableAlias();

            if (MemberType.IsEnum)
            {
                _parseObject = expression =>
                {
                    var raw = expression.Value();
                    return Enum.GetName(MemberType, raw);
                };

                _dbType = NpgsqlDbType.Varchar;
                PgType = "varchar";
            }
            else if (MemberType.IsDateTime())
            {
                PgType = "timestamp without time zone";
                _dbType = NpgsqlDbType.Timestamp;
            }
            else if (MemberType.IsDateTime())
            {
                PgType = "timestamp without time zone";
                _dbType = NpgsqlDbType.Timestamp;
            }
            else if (MemberType == typeof(DateTimeOffset) || MemberType == typeof(DateTimeOffset?))
            {
                PgType = "timestamp with time zone";
                _dbType = NpgsqlDbType.TimestampTz;
            }
            else
            {
                _dbType = TypeMappings.ToDbType(MemberType);
            }
        }

        public DuplicatedFieldRole Role { get; set; } = DuplicatedFieldRole.Search;

        public UpsertArgument UpsertArgument => new UpsertArgument
        {
            Arg = "arg_" + ColumnName.ToLower(),
            Column = ColumnName.ToLower(),
            PostgresType = PgType,
            Members = Members,
            DbType = _dbType
        };

        public string SelectionLocator => SqlLocator;

        public string ColumnName
        {
            get { return _columnName; }
            set
            {
                _columnName = value;
                SqlLocator = "d." + _columnName;
            }
        }

        public void WritePatch(DocumentMapping mapping, SchemaPatch patch)
        {
            patch.Updates.Apply(mapping, $"ALTER TABLE {mapping.Table.QualifiedName} ADD COLUMN {ColumnName} {PgType.Trim()};");

            patch.UpWriter.WriteLine($"update {mapping.Table.QualifiedName} set {UpdateSqlFragment()};");
        }

        // TODO -- have this take in CommandBuilder
        public string UpdateSqlFragment()
        {
            var jsonField = new JsonLocatorField("d.data", _enumStorage, Casing.Default, Members);
            // HOKEY, but I'm letting it pass for now.
            var sqlLocator = jsonField.SqlLocator.Replace("d.", "");

            return $"{ColumnName} = {sqlLocator}";
        }

        public object GetValue(Expression valueExpression)
        {
            return _parseObject(valueExpression);
        }

        public bool ShouldUseContainmentOperator()
        {
            return false;
        }

        public string LocatorFor(string rootTableAlias)
        {
            return $"{rootTableAlias}.{_columnName}";
        }

        public string SqlLocator { get; set; }

        public static DuplicatedField For<T>(EnumStorage enumStorage, Expression<Func<T, object>> expression)
        {
            var accessor = ReflectionHelper.GetAccessor(expression);

            // Hokey, but it's just for testing for now.
            if (accessor is PropertyChain)
            {
                throw new NotSupportedException("Not yet supporting deep properties yet. Soon.");
            }

            return new DuplicatedField(enumStorage, new MemberInfo[] { accessor.InnerProperty });
        }

        // I say you don't need a ForeignKey
        public virtual TableColumn ToColumn()
        {
            return new TableColumn(ColumnName, PgType);
        }
    }
}