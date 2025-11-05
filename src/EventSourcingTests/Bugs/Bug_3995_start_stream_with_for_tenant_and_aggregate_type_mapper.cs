using System.Threading;
using System.Threading.Tasks;
using EventSourcingTests.Aggregation;
using JasperFx.Events;
using JasperFx.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_3995_start_stream_with_for_tenant_and_aggregate_type_mapper : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3995_start_stream_with_for_tenant_and_aggregate_type_mapper(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task should_have_the_aggregate_type()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.AppendMode = EventAppendMode.Quick;
        });

        var action = theSession.ForTenant("one").Events.StartStream<LetterCounts>("AAABBBCD".ToLetterEvents());
        await theSession.SaveChangesAsync();

        var streamState = await theSession.ForTenant("one").Events.FetchStreamStateAsync(action.Id);
        streamState.AggregateType.ShouldBe(typeof(LetterCounts));
    }

    [Fact]
    public async Task do_it_with_projection_document_session()
    {
        StoreOptions(opts =>
        {
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Logger(new TestOutputMartenLogger(_output));
        });

        var batch = new ProjectionUpdateBatch(theStore.Options.Projections, (DocumentSessionBase)theSession, ShardExecutionMode.Continuous,
            CancellationToken.None);
        var sessionOptions = SessionOptions.ForDatabase(theStore.Options.Tenancy.Default.Database);
        using var session = new ProjectionDocumentSession(theStore, batch,
            sessionOptions, ShardExecutionMode.Continuous);

        var action = session.ForTenant("one").Events.StartStream<LetterCounts>("AAABBBCD".ToLetterEvents());

        await batch.WaitForCompletion();
        await session.ExecuteBatchAsync(batch, CancellationToken.None);

        var streamState = await theSession.ForTenant("one").Events.FetchStreamStateAsync(action.Id);
        streamState.AggregateType.ShouldBe(typeof(LetterCounts));


    }
}
