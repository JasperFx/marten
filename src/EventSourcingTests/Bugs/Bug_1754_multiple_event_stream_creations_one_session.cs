using System;

using Bug1754;

using Marten.Testing.Harness;
using Shouldly;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events;
using Marten.Events.Projections;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_1754_multiple_event_streams_in_one_session_fails : BugIntegrationContext
    {
        [Fact]
        public async Task should_be_able_to_handle_multiple_event_streams_in_one_session_async()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString; // easier to debug
                x.Projections.Snapshot<DataImportAggregate>(SnapshotLifecycle.Inline);
                x.Projections.Snapshot<DataItemAggregate>(SnapshotLifecycle.Inline);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();

            await using var session = documentStore.LightweightSession();

            var importStream = session.Events.StartStream<DataImportAggregate>("original", new DataImportStartedEvent {ByUser = Guid.NewGuid()});

            var importCount = 0;
            for (var i = 0; i < 3; i++)
            {
                session.Events.StartStream<DataItemAggregate>($"other{i + 1}" ,new CreateDataItem {ImportId = importStream.Key, Name = $"Data item {i}"});
                ++importCount;
            }

            session.Events.Append("original", new DataImportFinishedEvent {ImportCount = importCount});
            await session.SaveChangesAsync();

            await using var querySession = documentStore.QuerySession();
            var importAggregate = await querySession.LoadAsync<DataImportAggregate>(importStream.Key);
            importAggregate.ShouldNotBeNull();
            importAggregate.ImportCount.ShouldBe(importCount);
        }

        [Fact]
        public async Task should_be_able_to_handle_multiple_event_streams_in_one_session_sync()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString; // easier to debug
                x.Projections.Snapshot<DataImportAggregate>(SnapshotLifecycle.Inline);
                x.Projections.Snapshot<DataItemAggregate>(SnapshotLifecycle.Inline);
            });

            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();

            using var session = documentStore.LightweightSession();

            var importStream = session.Events.StartStream<DataImportAggregate>("original", new DataImportStartedEvent {ByUser = Guid.NewGuid()});

            var importCount = 0;
            for (var i = 0; i < 3; i++)
            {
                session.Events.StartStream<DataItemAggregate>($"other{i + 1}" ,new CreateDataItem {ImportId = importStream.Key, Name = $"Data item {i}"});
                ++importCount;
            }

            session.Events.Append("original", new DataImportFinishedEvent {ImportCount = importCount});
            await session.SaveChangesAsync();

            using var querySession = documentStore.QuerySession();
            var importAggregate = await querySession.LoadAsync<DataImportAggregate>(importStream.Key);
            importAggregate.ShouldNotBeNull();
            importAggregate.ImportCount.ShouldBe(importCount);
        }
    }
}

namespace Bug1754
{
    public class DataImportStartedEvent
    {
        public Guid ByUser { get; set; }
    }

    public class DataImportFinishedEvent
    {
        public int ImportCount { get; set; }
    }

    public class DataImportAggregate
    {
        public string Id { get; set; }

        public Guid StartedByUser { get; set; }
        public int ImportCount { get; set; }

        public void Apply(DataImportStartedEvent started) => StartedByUser = started.ByUser;

        public void Apply(DataImportFinishedEvent finished) => ImportCount = finished.ImportCount;
    }

    public class CreateDataItem
    {
        public string ImportId { get; set; }
        public string Name { get; set; }
    }

    public class DataItemAggregate
    {
        public string Id { get; set; }

        public string ImportId { get; set; }
        public string Name { get; set; }

        public void Apply(CreateDataItem dataItem)
        {
            ImportId = dataItem.ImportId;
            Name = dataItem.Name;
        }
    }
}
