using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using LamarCodeGeneration;
using Marten.Events.Archiving;
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
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Remotion.Linq;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events
{
    public interface IEventType
    {
        Type EventType { get; }
        string DotNetTypeName { get; set; }
        string EventTypeName { get; set; }
        string Alias { get; }
    }

    public abstract class EventMapping: IDocumentMapping, IEventType
    {
        protected readonly EventGraph _parent;
        protected readonly DocumentMapping _inner;
        private ISqlFragment _defaultWhereFragment;

        protected EventMapping(EventGraph parent, Type eventType)
        {
            TenancyStyle = parent.TenancyStyle;

            _parent = parent;
            DocumentType = eventType;

            EventTypeName = eventType.IsGenericType ? eventType.ShortNameInCode() : DocumentType.Name.ToTableAlias();
            IdMember = DocumentType.GetProperty(nameof(IEvent.Id));

            _inner = new DocumentMapping(eventType, parent.Options);

            DotNetTypeName = $"{eventType.FullName}, {eventType.Assembly.GetName().Name}";
            ISqlFragment filter = new WhereFragment($"d.type = '{EventTypeName}'");
            filter = filter.CombineAnd(IsNotArchivedFilter.Instance);
            if (parent.TenancyStyle == TenancyStyle.Conjoined)
            {
                filter = filter.CombineAnd(CurrentTenantFilter.Instance);
            }


            _defaultWhereFragment = filter;
        }

        Type IEventType.EventType => DocumentType;

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
            var extras = extraFilters(query).ToList();

            return query.CombineAnd(extras);
        }

        private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query)
        {
            yield return _defaultWhereFragment;
            if (!query.SpecifiesEventArchivalStatus())
            {
                yield return IsNotArchivedFilter.Instance;
            }

            var shouldBeTenanted = _parent.TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant();
            if (shouldBeTenanted)
            {
                yield return CurrentTenantFilter.Instance;
            }
        }

        public ISqlFragment DefaultWhereFragment()
        {
            return _defaultWhereFragment;
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
            _tableName = schemaName == SchemaConstants.DefaultSchema ? "mt_events" : $"{schemaName}.mt_events";

            _idType = parent.StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
        }

        public void TruncateDocumentStorage(ITenant tenant)
        {
            tenant.RunSql($"delete from table {_tableName} where type = '{Alias}'");
        }

        public Task TruncateDocumentStorageAsync(ITenant tenant)
        {
            return tenant.RunSqlAsync($"delete from table {_tableName} where type = '{Alias}'");
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
                return _serializer.FromJson<TEvent>(reader, 0);
            }

            public async Task<TEvent> ResolveAsync(DbDataReader reader, CancellationToken token)
            {
                var doc = await _serializer.FromJsonAsync<TEvent>(reader, 0, token);

                return doc;
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

        public IDeletion HardDeleteForDocument(T document, ITenant tenant)
        {
            throw new NotSupportedException();
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
