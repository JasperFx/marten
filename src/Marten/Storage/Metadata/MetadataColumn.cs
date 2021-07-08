using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Expressions;
using LamarCodeGeneration;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using FindMembers = Marten.Linq.Parsing.FindMembers;

namespace Marten.Storage.Metadata
{

    public abstract class MetadataColumn : TableColumn
    {
        public Type DotNetType { get; }

        protected MetadataColumn(string name, string type, Type dotNetType) : base(name, type)
        {
            DotNetType = dotNetType;
        }

        internal abstract Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader, CancellationToken token);
        internal abstract void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader);

        public abstract MemberInfo Member { get; set; }

        /// <summary>
        /// Is this metadata column enabled?
        /// </summary>
        public bool Enabled { get; set; } = true;

        internal virtual void RegisterForLinqSearching(DocumentMapping mapping)
        {
            if (!Enabled || Member == null) return;



            mapping.DuplicateField(new MemberInfo[] {Member}, columnName: Name)
                .OnlyForSearching = true;
        }

        public bool EnabledWithMember()
        {
            return Enabled && Member != null;
        }

        internal virtual UpsertArgument ToArgument()
        {
            return new UpsertArgument
            {
                Arg = "arg_" + Name,
                Column = Name,
                DbType = PostgresqlProvider.Instance.ToParameterType(DotNetType),
                PostgresType = Type,
                Members = new MemberInfo[]{Member}

            };
        }

        protected void setMemberFromReader(GeneratedType generatedType, GeneratedMethod async, GeneratedMethod sync, int index,
            DocumentMapping mapping)
        {
            if (Member == null) return;

            sync.IfDbReaderValueIsNotNull(index, () =>
            {
                sync.AssignMemberFromReader(generatedType, index, mapping.DocumentType, Member.Name);
            });

            async.IfDbReaderValueIsNotNullAsync(index, () =>
            {
                async.AssignMemberFromReaderAsync(generatedType, index, mapping.DocumentType, Member.Name);
            });
        }
    }

    internal abstract class MetadataColumn<T> : MetadataColumn
    {
        private readonly Action<DocumentMetadata, T> _setter;
        private MemberInfo _member;
        private readonly string _memberName;

        protected MetadataColumn(string name, Expression<Func<DocumentMetadata, T>> property) : base(name, PostgresqlProvider.Instance.GetDatabaseType(typeof(T), EnumStorage.AsInteger), typeof(T))
        {
            var member = FindMembers.Determine(property).Last();
            _memberName = member.Name;
            _setter = LambdaBuilder.Setter<DocumentMetadata, T>(member);
        }

        internal override async Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(index, token)) return;

            var value = await reader.GetFieldValueAsync<T>(index, token);
            _setter(metadata, value);
        }

        internal override void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader)
        {
            if (reader.IsDBNull(index)) return;

            var value = reader.GetFieldValue<T>(index);
            _setter(metadata, value);
        }

        public override MemberInfo Member
        {
            get { return _member; }
            set
            {
                if (value != null)
                {

                    if (value.GetRawMemberType() != typeof(T))
                        throw new ArgumentOutOfRangeException(nameof(value),
                            $"The {_memberName} member has to be of type {typeof(T).NameInCode()}");

                    _member = value;
                    Enabled = true;
                }
            }
        }
    }
}
