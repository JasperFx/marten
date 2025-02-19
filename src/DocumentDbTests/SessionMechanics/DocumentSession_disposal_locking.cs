using System;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class DocumentSession_disposal_locking
{
    [Fact]
    public async Task throw_disposed_ex_after_disposed()
    {
        var store = DocumentStore.For(_ => _.Connection(ConnectionSource.ConnectionString));

        var session = store.LightweightSession();
        session.Dispose();

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
        {
            await session.LoadAsync<User>(Guid.NewGuid());
        });


    }
}
