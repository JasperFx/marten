using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class updating_by_batch : OneOffConfigurationsContext
{
    [Fact]
    public void can_make_updates_with_more_than_one_batch()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        StoreOptions(_ => _.UpdateBatchSize = 10);

        using var session = theStore.LightweightSession();
        session.Store(targets);
        session.SaveChanges();

        session.Query<Target>().Count().ShouldBe(100);
    }


    [Fact]
    public async Task can_make_updates_with_more_than_one_batch_async()
    {
        StoreOptions(_ => { _.UpdateBatchSize = 10; });

        var targets = Target.GenerateRandomData(100).ToArray();

        await using var session = theStore.LightweightSession();
        session.Store(targets);
        await session.SaveChangesAsync();

        (await session.Query<Target>().CountAsync()).ShouldBe(100);
    }

    [Fact]
    public void can_delete_and_make_updates_with_more_than_one_batch_GH_987()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        StoreOptions(_ =>
        {
            _.UpdateBatchSize = 10;
        });

        using var session = theStore.LightweightSession();
        session.DeleteWhere<Target>(t => t.Id != Guid.Empty);
        session.Store(targets);

        session.SaveChanges();

        session.Query<Target>().Count().ShouldBe(100);
    }

    [Fact]
    public async Task can_delete_and_make_updates_with_more_than_one_batch_async()
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        StoreOptions(_ => _.UpdateBatchSize = 10);

        await using var session = theStore.LightweightSession();
        session.DeleteWhere<Target>(x => x.Id != Guid.Empty);
        session.Store(targets);

        await session.SaveChangesAsync();

        var t = await session.Query<Target>().CountAsync();
        t.ShouldBe(100);
    }
}