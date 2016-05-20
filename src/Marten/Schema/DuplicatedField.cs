using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Baseline.Reflection;
using Marten.Generation;
using Marten.Linq;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;


namespace Marten.Schema
{
    public class DuplicatedField : Field, IField
    {
        private string _columnName;
        private readonly Func<Expression, object> _parseObject = expression => expression.Value();
        private readonly EnumStorage _enumStorage;
        private readonly NpgsqlDbType _dbType;

        public static DuplicatedField For<T>(EnumStorage enumStorage, Expression<Func<T, object>> expression)
        {
            var accessor = ReflectionHelper.GetAccessor(expression);

            // Hokey, but it's just for testing for now.
            if (accessor is PropertyChain)
            {
                throw new NotSupportedException("Not yet supporting deep properties yet. Soon.");
            }


            return new DuplicatedField(enumStorage, new MemberInfo[] {accessor.InnerProperty});

            
        }

        public DuplicatedField(EnumStorage enumStorage, MemberInfo[] memberPath) : base(memberPath)
        {
            _enumStorage = enumStorage;
            _dbType = TypeMappings.ToDbType(MemberType);



            ColumnName = MemberName.ToTableAlias();

            if (MemberType.IsEnum)
            {
                typeof(EnumRegistrar<>).CloseAndBuildAs<IEnumRegistrar>(MemberType).Register();

                

                _parseObject = expression =>
                {
                    var raw = expression.Value();
                    return Enum.GetName(MemberType, raw);
                };

                _dbType = NpgsqlDbType.Varchar;
                PgType = "varchar";

            }

            
        }

        internal interface IEnumRegistrar
        {
            void Register();
        }

        internal class EnumRegistrar<T> : IEnumRegistrar where T : struct
        {
            public void Register()
            {
                NpgsqlConnection.RegisterEnumGlobally<T>();
            }
        }

        public string ColumnName
        {
            get { return _columnName; }
            set
            {
                _columnName = value;
                SqlLocator = "d." + _columnName;
            }
        }

        // TODO -- think this one might have to change w/ FK's
        public void WritePatch(DocumentMapping mapping, Action<string> executeSql)
        {
            executeSql($"ALTER TABLE {mapping.Table.QualifiedName} ADD COLUMN {ColumnName} {PgType};");

            var jsonField = new JsonLocatorField(_enumStorage, Members);

            // HOKEY, but I'm letting it pass for now.
            var sqlLocator = jsonField.SqlLocator.Replace("d.", "");

            executeSql($"update {mapping.Table.QualifiedName} set {ColumnName} = {sqlLocator}");

        }

        public object GetValue(Expression valueExpression)
        {
            return _parseObject(valueExpression);
        }

        public bool ShouldUseContainmentOperator()
        {
            return false;
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

        // I say you don't need a ForeignKey 
        public virtual TableColumn ToColumn()
        {
            return new TableColumn(ColumnName, PgType);
        }


        public string SqlLocator { get; private set; }

    }
}