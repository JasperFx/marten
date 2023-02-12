using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class identity_map_with_records: IntegrationContext
{
    public identity_map_with_records(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task identity_map_operations_with_records_should_work()
    {
        var guid = Guid.NewGuid();
        var guid2 = Guid.NewGuid();

        await using var session = theStore.LightweightSession();
        {
            session.Store(new MyEntity(guid, Guid.NewGuid()));
            await session.SaveChangesAsync();
        }

        await using var session2 = theStore.LightweightSession();
        {
            var entity = await session2.LoadAsync<MyEntity>(guid);
            var updated = entity with { RandomId = Guid.NewGuid() };
            session2.Store(updated);
            await session2.SaveChangesAsync();
        }
    }

    public record MyEntity(Guid Id, Guid RandomId);
}
