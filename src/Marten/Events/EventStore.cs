using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Archiving;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Storage;
using Npgsql;
using Weasel.Core;

#nullable enable
namespace Marten.Events
{
    internal partial class EventStore: QueryEventStore, IEventStore
    {
        private readonly DocumentSessionBase _session;
        private readonly DocumentStore _store;

        public EventStore(DocumentSessionBase session, DocumentStore store, Tenant tenant): base(session, store, tenant)
        {
            _session = session;
            _store = store;
        }


    }
}
