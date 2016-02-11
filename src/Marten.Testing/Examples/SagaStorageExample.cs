using System;
using System.Data;

namespace Marten.Testing.Examples
{
    public class SagaStorageExample
    {
        // SAMPLE: serializable-saga-transaction
public class MySagaState
{
    public Guid Id;
}

public void execute_saga(IDocumentStore store, Guid sagaId)
{
    // The session below will open its connection and start a 
    // serializable transaction
    using (var session = store.DirtyTrackedSession(IsolationLevel.Serializable))
    {
        var state = session.Load<MySagaState>(sagaId);

        // do some work against the saga

        session.SaveChanges();
    }
}
        // ENDSAMPLE
    }


}