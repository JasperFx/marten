using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Internal.Sessions
{
    internal class NestedTenantQuerySession : QuerySession, ITenantQueryOperations
    {
        private readonly QuerySession _parent;

        internal NestedTenantQuerySession(QuerySession parent, Tenant tenant) : base((DocumentStore) parent.DocumentStore, parent.SessionOptions, parent._connection)
        {
            Listeners.AddRange(parent.Listeners);
            _parent = parent;
            Versions = parent.Versions;
            ItemMap = parent.ItemMap;

            // TODO -- express the TenantId here!!!
        }

        public IQuerySession Parent => _parent;

        protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return _parent.selectStorage(provider);
        }

        public string TenantId => Tenant.TenantId;
    }
}
