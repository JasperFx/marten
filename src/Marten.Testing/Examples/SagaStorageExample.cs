using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Testing.Examples;

public class SagaStorageExample
{
    #region sample_serializable-saga-transaction

    public class MySagaState
    {
        public Guid Id { get; set; }
    }

    public async Task execute_saga_serializable(IDocumentStore store, Guid sagaId, CancellationToken ct)
    {
        // The session below will open its connection and start a
        // serializable transaction avoiding blocking calls
        await using var session = await store.LightweightSerializableSessionAsync(ct);
        var state = await session.LoadAsync<MySagaState>(sagaId, ct);

        // do some work against the saga

        await session.SaveChangesAsync(ct);
    }

    #endregion

    #region sample_saga-transaction

    public async Task execute_saga(IDocumentStore store, Guid sagaId, CancellationToken ct)
    {
        // The session below will open its connection and start a
        // snapshot transaction
        await using var session = store.LightweightSession(IsolationLevel.Snapshot);
        var state = await session.LoadAsync<MySagaState>(sagaId, ct);

        // do some work against the saga

        await session.SaveChangesAsync(ct);
    }

    #endregion
}
