using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Linq.Filters;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq;

namespace Marten.Events
{
    public abstract class EventMapping: IDocumentMapping
    {
        protected readonly EventGraph _parent;
        protected readonly DocumentMapping _inner;

        protected EventMapping(EventGraph parent, Type eventType)
        {
            TenancyStyle = parent.TenancyStyle;

            _parent = parent;
            DocumentType = eventType;

            EventTypeName = eventType.IsGenericType ? eventType.ShortNameInCode() : DocumentType.Name.ToTableAlias();
            IdMember = DocumentType.GetProperty(nameof(IEvent.Id));

            _inner = new DocumentMapping(eventType, parent.Options);

            DotNetTypeName = $"{eventType.FullName}, {eventType.Assembly.GetName().Name}";
        }

        public string DotNetTypeName { get; set; }


        IDocumentMapping IDocumentMapping.Root => this;
        public Type DocumentType { get; }
        public string EventTypeName { get; set; }
        public string Alias => EventTypeName;
        public MemberInfo IdMember { get; }
        public NpgsqlDbType IdType { get; } = NpgsqlDbType.Uuid;
        public TenancyStyle TenancyStyle { get; } = TenancyStyle.Single;

        Type IDocumentMapping.IdType => typeof(Guid);

        public DbObjectName TableName => new DbObjectName(_parent.DatabaseSchemaName, "mt_events");
        public DuplicatedField[] DuplicatedFields { get; }
        public DeleteStyle DeleteStyle { get; }

        public PropertySearching PropertySearching { get; } = PropertySearching.JSON_Locator_Only;

        public string[] SelectFields()
        {
            return new[] { "id", "data" };
        }

        public IField FieldFor(Expression expression)
        {
            return FieldFor(FindMembers.Determine(expression));
        }

        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            return _inner.FieldFor(members);
        }

        public IField FieldFor(MemberInfo member)
        {
            return _inner.FieldFor(member);
        }

        public IField FieldFor(string memberName)
        {
            throw new NotSupportedException();
        }

        public ISqlFragment FilterDocuments(QueryModel model, ISqlFragment query)
        {
            return new CompoundWhereFragment("and", DefaultWhereFragment(), query);
        }

        public ISqlFragment DefaultWhereFragment()
        {
            return new WhereFragment($"d.type = '{EventTypeName}'");
        }

        public abstract IEvent Wrap(object data);
    }

    public class EventMapping<T>: EventMapping, IDocumentStorage<T> where T : class
    {
        private readonly string _tableName;
        private Type _idType;

        public EventMapping(EventGraph parent) : base(parent, typeof(T))
        {
            var schemaName = parent.DatabaseSchemaName;
            _tableName = schemaName == StoreOptions.DefaultDatabaseSchemaName ? "mt_events" : $"{schemaName}.mt_events";

            _idType = parent.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
        }

        public void TruncateDocumentStorage(ITenant tenant)
        {
            tenant.RunSql($"delete from table {_tableName} where type = '{Alias}'");
        }

        public bool UseOptimisticConcurrency { get; } = false;
        public IOperationFragment DeleteFragment => throw new NotSupportedException();
        public IOperationFragment HardDeleteFragment { get; }

        string ISelectClause.FromObject => _tableName;

        Type ISelectClause.SelectedType => typeof(T);

        void ISelectClause.WriteSelectClause(CommandBuilder sql)
        {
            sql.Append("select data from ");
            sql.Append(_tableName);
            sql.Append(" as d");
        }

        ISelector ISelectClause.BuildSelector(IMartenSession session)
        {
            return new EventSelector<T>(session.Serializer);
        }

        IQueryHandler<TResult> ISelectClause.BuildHandler<TResult>(IMartenSession session, Statement topStatement, Statement currentStatement)
        {
            var selector = new EventSelector<T>(session.Serializer);

            return LinqHandlerBuilder.BuildHandler<T, TResult>(selector, topStatement);
        }

        internal class EventSelector<TEvent>: ISelector<TEvent>
        {
            private readonly ISerializer _serializer;

            public EventSelector(ISerializer serializer)
            {
                _serializer = serializer;
            }

            public TEvent Resolve(DbDataReader reader)
            {
                using var json = reader.GetStream(0);
                return _serializer.FromJson<TEvent>(json);
            }

            public Task<TEvent> ResolveAsync(DbDataReader reader, CancellationToken token)
            {
                using var json = reader.GetStream(0);
                var doc = _serializer.FromJson<TEvent>(json);

                return Task.FromResult(doc);
            }
        }

        ISelectClause ISelectClause.UseStatistics(QueryStatistics statistics)
        {
            throw new NotSupportedException();
        }

        Type IDocumentStorage.SourceType => typeof(IEvent);

        object IDocumentStorage<T>.IdentityFor(T document)
        {
            throw new NotSupportedException();
        }

        Type IDocumentStorage.IdType => _idType;
        public IFieldMapping Fields => _inner;

        Guid? IDocumentStorage<T>.VersionFor(T document, IMartenSession session)
        {
            throw new NotSupportedException();
        }

        void IDocumentStorage<T>.Store(IMartenSession session, T document)
        {
            throw new NotSupportedException();
        }

        void IDocumentStorage<T>.Store(IMartenSession session, T document, Guid? version)
        {
            throw new NotSupportedException();
        }

        void IDocumentStorage<T>.Eject(IMartenSession session, T document)
        {
            throw new NotSupportedException();
        }

        IStorageOperation IDocumentStorage<T>.Update(T document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        IStorageOperation IDocumentStorage<T>.Insert(T document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        IStorageOperation IDocumentStorage<T>.Upsert(T document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        IStorageOperation IDocumentStorage<T>.Overwrite(T document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        IDeletion IDocumentStorage<T>.DeleteForDocument(T document)
        {
            throw new NotSupportedException();
        }

        public IDeletion DeleteForDocument(T document, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        void IDocumentStorage<T>.EjectById(IMartenSession session, object id)
        {
            // Nothing
        }

        void IDocumentStorage<T>.RemoveDirtyTracker(IMartenSession session, object id)
        {
            // Nothing
        }

        public IDeletion HardDeleteForDocument(T document)
        {
            throw new NotImplementedException();
        }

        public IDeletion HardDeleteForDocument(T document, ITenant tenant)
        {
            throw new NotImplementedException();
        }

        public override IEvent Wrap(object data)
        {
            return new Event<T>((T) data)
            {
                EventTypeName = EventTypeName,
                DotNetTypeName = DotNetTypeName
            };
        }
    }
}
