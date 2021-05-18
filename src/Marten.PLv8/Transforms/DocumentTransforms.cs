using System;
using System.Linq.Expressions;
using Marten.Internal.Sessions;
using Marten.Linq.Filters;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.PLv8.Transforms
{
    internal class DocumentTransforms: IDocumentTransforms, IDisposable
    {
        private readonly DocumentStore _store;
        private readonly ITenant _tenant;

        public DocumentTransforms(DocumentStore store, ITenant tenant)
        {
            _store = store;
            _tenant = tenant;

            Session = (DocumentSessionBase)_store.LightweightSession();
        }

        public void Dispose()
        {
            Session?.Dispose();
        }

        public DocumentSessionBase Session { get; }

        public void All<T>(string transformName)
        {
            var transform = _tenant.TransformFor(transformName);
            var storage = _tenant.StorageFor<T>();

            var operation = new DocumentTransformOperationFragment(storage, transform);
            var statement = new StatementOperation(storage, operation);

            // To bake in the default document filtering here
            statement.CompileLocal(Session);
            Session.QueueOperation(statement);
        }

        public void Tenant<T>(string transformName, string tenantId)
        {
            Where<T>(transformName, x => x.TenantIsOneOf(tenantId));
        }

        public void Tenants<T>(string transformName, params string[] tenantIds)
        {
            Where<T>(transformName, x => x.TenantIsOneOf(tenantIds));
        }

        public void Where<T>(string transformName, Expression<Func<T, bool>> @where)
        {
            var transform = _tenant.TransformFor(transformName);

            var storage = Session.StorageFor<T>();
            var operation = new DocumentTransformOperationFragment(storage, transform);

            var statement = new StatementOperation(storage, operation);
            statement.ApplyFiltering(Session, @where);
            Session.QueueOperation(statement);
        }


        public void Document<T>(string transformName, string id)
        {
            transformOne<T>(transformName, new ByStringFilter(id));
        }

        private void transformOne<T>(string transformName, ISqlFragment filter)
        {
            var transform = _tenant.TransformFor(transformName);

            var storage = Session.StorageFor<T>();
            var operation = new DocumentTransformOperationFragment(storage, transform);

            var statement = new StatementOperation(storage, operation) {Where = filter};

            // To bake in the default document filtering here
            statement.CompileLocal(Session);
            Session.QueueOperation(statement);
        }

        public void Document<T>(string transformName, int id)
        {
            transformOne<T>(transformName, new ByIntFilter(id));
        }

        public void Document<T>(string transformName, long id)
        {
            transformOne<T>(transformName, new ByLongFilter(id));
        }

        public void Document<T>(string transformName, Guid id)
        {
            transformOne<T>(transformName, new ByGuidFilter(id));
        }
    }
}
