using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Util;
using FindMembers = Marten.Linq.Parsing.FindMembers;
using LambdaBuilder = Marten.Util.LambdaBuilder;

namespace Marten.Storage.Metadata
{

    public abstract class MetadataColumn : TableColumn
    {
        public Type DotNetType { get; }

        protected MetadataColumn(string name, string type, Type dotNetType) : base(name, type)
        {
            DotNetType = dotNetType;
        }

        public abstract Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader, CancellationToken token);
        public abstract void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader);

        public abstract MemberInfo Member { get; set; }
        public bool Enabled { get; set; } = true;

        public bool EnabledWithMember()
        {
            return Enabled && Member != null;
        }

        public virtual UpsertArgument ToArgument()
        {
            return new UpsertArgument
            {
                Arg = "arg_" + Name,
                Column = Name,
                DbType = TypeMappings.ToDbType(DotNetType),
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

        protected MetadataColumn(string name, Expression<Func<DocumentMetadata, T>> property) : base(name, TypeMappings.GetPgType(typeof(T), EnumStorage.AsInteger), typeof(T))
        {
            var member = FindMembers.Determine(property).Last();
            _memberName = member.Name;
            _setter = LambdaBuilder.Setter<DocumentMetadata, T>(member);
        }

        public override async Task ApplyAsync(IMartenSession martenSession, DocumentMetadata metadata, int index,
            DbDataReader reader, CancellationToken token)
        {
            if (await reader.IsDBNullAsync(index, token)) return;

            var value = await reader.GetFieldValueAsync<T>(index, token);
            _setter(metadata, value);
        }

        public override void Apply(IMartenSession martenSession, DocumentMetadata metadata, int index,
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
