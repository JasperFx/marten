using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class capturing_event_versions_on_existing_streams_after_append: IntegrationContext
{
    public class RecordingSessionLogger: IMartenSessionLogger
    {
        public void LogSuccess(NpgsqlCommand command)
        {
        }

        public void LogFailure(NpgsqlCommand command, Exception ex)
        {
        }

        public void LogSuccess(NpgsqlBatch batch)
        {

        }

        public void LogFailure(NpgsqlBatch batch, Exception ex)
        {

        }

        public void LogFailure(Exception ex, string message)
        {

        }

        public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
        {
            LastCommit = commit.Clone();
        }

        public void OnBeforeExecute(NpgsqlCommand command)
        {

        }

        public void OnBeforeExecute(NpgsqlBatch batch)
        {

        }

        public IChangeSet LastCommit { get; set; }
    }

    [Fact]
    public async Task running_synchronously()
    {
        var logger = new RecordingSessionLogger();

        Guid streamId = Guid.NewGuid();

        using (var session = theStore.LightweightSession())
        {
            session.Logger = logger;

            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.StartStream<Quest>(streamId, joined, departed);
            await session.SaveChangesAsync();

            var events = logger.LastCommit.GetEvents().ToArray();
            events.Select(x => x.Version)
                .ShouldHaveTheSameElementsAs(1, 2);

            events.Each(x => x.Sequence.ShouldBeGreaterThan(0L));

            events.Select(x => x.Sequence).Distinct().Count().ShouldBe(2);
        }

        using (var session = theStore.LightweightSession())
        {
            session.Logger = logger;

            var joined2 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed2 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(streamId, joined2, departed2);
            await session.SaveChangesAsync();

            logger.LastCommit.GetEvents().Select(x => x.Version)
                .ShouldHaveTheSameElementsAs(3, 4);
        }

        using (var session = theStore.LightweightSession())
        {
            session.Logger = logger;

            var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(streamId, joined3, departed3);
            await session.SaveChangesAsync();

            logger.LastCommit.GetEvents().Select(x => x.Version)
                .ShouldHaveTheSameElementsAs(5, 6);
        }
    }

    [Fact]
    public async Task running_asynchronously()
    {
        var logger = new RecordingSessionLogger();
        Guid streamId = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = logger;

            var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
            var departed = new MembersDeparted { Members = new[] { "Thom" } };

            session.Events.StartStream<Quest>(streamId, joined, departed);
            await session.SaveChangesAsync();

            logger.LastCommit.GetEvents().Select(x => x.Version)
                .ShouldHaveTheSameElementsAs(1, 2);
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = logger;

            var joined2 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed2 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(streamId, joined2, departed2);
            await session.SaveChangesAsync();

            logger.LastCommit.GetEvents().Select(x => x.Version)
                .ShouldHaveTheSameElementsAs(3, 4);
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Logger = logger;

            var joined3 = new MembersJoined { Members = new[] { "Egwene" } };
            var departed3 = new MembersDeparted { Members = new[] { "Perrin" } };

            session.Events.Append(streamId, joined3, departed3);
            await session.SaveChangesAsync();

            var events = logger.LastCommit.GetEvents().ToArray();
            events.Select(x => x.Version)
                .ShouldHaveTheSameElementsAs(5, 6);

            events.Each(x => x.Sequence.ShouldBeGreaterThan(0L));

            events.Select(x => x.Sequence).Distinct().Count().ShouldBe(2);
        }
    }

    public capturing_event_versions_on_existing_streams_after_append(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
