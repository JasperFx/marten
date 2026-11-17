using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_6034_batch_items_after_a_failed_execute: IntegrationContext
{
    public Bug_6034_batch_items_after_a_failed_execute(DefaultStoreFixture fixture): base(fixture)
    {
    }

    private static async Task shouldBeFinished(Task item)
    {
        // Before #6034 these never finished, so give them a bounded wait rather than hang the test run
        await Task.WhenAny(item, Task.Delay(TimeSpan.FromSeconds(5)));
        item.IsCompleted.ShouldBeTrue("the batch item never completed after Execute() failed");
    }

    [Fact]
    public async Task every_item_faults_when_the_batch_fails_at_its_first_statement()
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var broken = batch.Query<Target>("where no_such_column = 1");
        var load = batch.Load<Target>(target.Id);

        var failure = await Should.ThrowAsync<Exception>(() => batch.Execute());

        await shouldBeFinished(broken);
        await shouldBeFinished(load);

        broken.IsFaulted.ShouldBeTrue();
        load.IsFaulted.ShouldBeTrue();
        (await Should.ThrowAsync<Exception>(() => load)).ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task an_item_read_before_the_failure_keeps_its_result()
    {
        var target = Target.Random();
        theSession.Store(target);
        await theSession.SaveChangesAsync();

        var batch = theSession.CreateBatchQuery();
        var load = batch.Load<Target>(target.Id);
        var broken = batch.Query<Target>("where no_such_column = 1");
        var after = batch.Load<Target>(target.Id);

        await Should.ThrowAsync<Exception>(() => batch.Execute());

        await shouldBeFinished(load);
        await shouldBeFinished(broken);
        await shouldBeFinished(after);

        (await load).ShouldNotBeNull().Id.ShouldBe(target.Id);
        broken.IsFaulted.ShouldBeTrue();
        after.IsFaulted.ShouldBeTrue();
    }

    [Fact]
    public async Task items_are_cancelled_when_execute_is_cancelled()
    {
        var batch = theSession.CreateBatchQuery();
        var load = batch.Load<Target>(Guid.NewGuid());

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => batch.Execute(cancellation.Token));

        await shouldBeFinished(load);
        load.IsCanceled.ShouldBeTrue();
    }
}
