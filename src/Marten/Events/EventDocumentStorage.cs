using System;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Marten.Util;
using Remotion.Linq;

namespace Marten.Events
{


    public class EventDocumentStorage : IDocumentStorage<IEvent>
    {
        private readonly EventGraph _graph;
        private readonly EventQueryMapping _mapping;
        private readonly IEventSelector _selector;

        public EventDocumentStorage(EventGraph graph, EventQueryMapping mapping, ISerializer serializer)
        {
            _graph = graph;
            _mapping = mapping;

            FromObject = _mapping.TableName.QualifiedName;
            Fields = mapping;

            if (graph.StreamIdentity == StreamIdentity.AsGuid)
            {
                IdType = typeof(Guid);
                _selector = new EventSelector(graph, serializer);
            }
            else
            {
                IdType = typeof(string);
                _selector = new StringIdentifiedEventSelector(graph, serializer);
            }
        }

        public TenancyStyle TenancyStyle => _mapping.TenancyStyle;

        public IDeletion DeleteForDocument(IEvent document, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public void EjectById(IMartenSession session, object id)
        {
            // Nothing
        }

        public void RemoveDirtyTracker(IMartenSession session, object id)
        {
            // Nothing
        }

        public string FromObject { get; }
        public Type SelectedType => typeof(IEvent);
        public void WriteSelectClause(CommandBuilder sql)
        {
            _selector.WriteSelectClause(sql);
        }

        public string[] SelectFields()
        {
            return _selector.SelectFields();
        }

        public ISelector BuildSelector(IMartenSession session)
        {
            return _selector;
        }

        public IQueryHandler<T> BuildHandler<T>(IMartenSession session, Statement topStatement, Statement currentStatement)
        {
            return LinqHandlerBuilder.BuildHandler<IEvent, T>(_selector, topStatement);
        }

        public ISelectClause UseStatistics(QueryStatistics statistics)
        {
            throw new NotSupportedException();
        }

        public Type SourceType => typeof(IEvent);
        public IFieldMapping Fields { get; }
        public ISqlFragment FilterDocuments(QueryModel model, ISqlFragment query)
        {
            return query;
        }

        public ISqlFragment DefaultWhereFragment()
        {
            return null;
        }

        public bool UseOptimisticConcurrency { get; } = false;
        public IOperationFragment DeleteFragment => throw new NotSupportedException();
        public DuplicatedField[] DuplicatedFields { get; } = new DuplicatedField[0];
        public DbObjectName TableName => _mapping.TableName;
        public Type DocumentType => typeof(IEvent);

        public object IdentityFor(IEvent document)
        {
            return _graph.StreamIdentity == StreamIdentity.AsGuid ? (object) document.Id : document.StreamKey;
        }

        public Type IdType { get; }
        public Guid? VersionFor(IEvent document, IMartenSession session)
        {
            return null;
        }

        public void Store(IMartenSession session, IEvent document)
        {
            // Nothing
        }

        public void Store(IMartenSession session, IEvent document, Guid? version)
        {
            // Nothing
        }

        public void Eject(IMartenSession session, IEvent document)
        {
            // Nothing
        }

        public IStorageOperation Update(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IStorageOperation Insert(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IStorageOperation Upsert(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotImplementedException();
        }

        public IStorageOperation Overwrite(IEvent document, IMartenSession session, ITenant tenant)
        {
            throw new NotSupportedException();
        }

        public IDeletion DeleteForDocument(IEvent document)
        {
            throw new NotSupportedException();
        }

    }
}
