using System;
using Baseline;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Internal.Sessions
{
    internal class NestedTenantSession : DocumentSessionBase, ITenantOperations
    {
        private readonly DocumentSessionBase _parent;

        internal NestedTenantSession(DocumentSessionBase parent, ITenant tenant) : base((DocumentStore) parent.DocumentStore, parent.SessionOptions, parent.Database, tenant, parent._workTracker)
        {
            Listeners.AddRange(parent.Listeners);
            _parent = parent;
            Versions = parent.Versions;
            ItemMap = parent.ItemMap;
        }

        public IDocumentSession Parent => _parent;

        protected internal override void ejectById<T>(long id)
        {
            _parent.ejectById<T>(id);
        }

        protected internal override void ejectById<T>(int id)
        {
            _parent.ejectById<T>(id);
        }

        protected internal override void ejectById<T>(Guid id)
        {
            _parent.ejectById<T>(id);
        }

        protected internal override void ejectById<T>(string id)
        {
            _parent.ejectById<T>(id);
        }

        protected internal override void processChangeTrackers()
        {
            _parent.processChangeTrackers();
        }

        protected internal override void resetDirtyChecking()
        {
            _parent.resetDirtyChecking();
        }

        protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return _parent.selectStorage(provider);
        }

        public string TenantId => Tenant.TenantId;
    }
}
