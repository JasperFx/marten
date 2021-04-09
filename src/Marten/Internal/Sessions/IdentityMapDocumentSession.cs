using System;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Storage;
#nullable enable
namespace Marten.Internal.Sessions
{
    public class IdentityMapDocumentSession: DocumentSessionBase
    {
        public IdentityMapDocumentSession(DocumentStore store, SessionOptions sessionOptions, IManagedConnection database, ITenant tenant) : base(store, sessionOptions, database, tenant)
        {
        }

        protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
        {
            return provider.IdentityMap;
        }

        protected internal override void ejectById<T>(long id)
        {
            StorageFor<T>().EjectById(this, id);
        }

        protected internal override void ejectById<T>(int id)
        {
            StorageFor<T>().EjectById(this, id);
        }

        protected internal override void ejectById<T>(Guid id)
        {
            StorageFor<T>().EjectById(this, id);
        }

        protected internal override void ejectById<T>(string id)
        {
            StorageFor<T>().EjectById(this, id);
        }
    }
}
