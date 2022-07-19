using Baseline;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Internal.Sessions
{
    internal class NestedTenantQuerySession : QuerySession, ITenantQueryOperations
    {
        private readonly QuerySession _parent;

        internal NestedTenantQuerySession(QuerySession parent, Tenant tenant) : base((DocumentStore) parent.DocumentStore, parent.SessionOptions, parent._connection, tenant)
        {
            Listeners.AddRange(parent.Listeners);
            _parent = parent;
            Versions = parent.Versions;
            ItemMap = parent.ItemMap;
        }

        public IQuerySession Parent => _parent;

        protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return _parent.selectStorage(provider);
        }
    }
}
