using System;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Util;
using FindMembers = Marten.Linq.Parsing.FindMembers;
using LambdaBuilder = Marten.Util.LambdaBuilder;

namespace Marten.Storage
{

    public abstract class MetadataColumn : TableColumn
    {
        protected MetadataColumn(string name, string type) : base(name, type)
        {
        }

        protected MetadataColumn(string name, string type, string directive) : base(name, type, directive)
        {
        }

        public abstract Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token);
        public abstract void Apply(DocumentMetadata metadata, int index, DbDataReader reader);

        public abstract MemberInfo Member { get; set; }
        public bool Enabled { get; set; } = true;
    }

    internal abstract class MetadataColumn<T> : MetadataColumn
    {
        private readonly Action<DocumentMetadata, T> _setter;
        private MemberInfo _member;
        private readonly string _memberName;

        protected MetadataColumn(string name, Expression<Func<DocumentMetadata, T>> property) : base(name, TypeMappings.GetPgType(typeof(T), EnumStorage.AsInteger))
        {
            var member = FindMembers.Determine(property).Last();
            _memberName = member.Name;
            _setter = LambdaBuilder.Setter<DocumentMetadata, T>(member);
        }

        public override async Task ApplyAsync(DocumentMetadata metadata, int index, DbDataReader reader, CancellationToken token)
        {
            var value = await reader.GetFieldValueAsync<T>(index, token);
            _setter(metadata, value);
        }

        public override void Apply(DocumentMetadata metadata, int index, DbDataReader reader)
        {
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
