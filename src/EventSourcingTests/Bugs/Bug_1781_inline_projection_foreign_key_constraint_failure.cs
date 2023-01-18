using System;
using System.Threading.Tasks;

using Bug1781;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;

using Shouldly;

using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_1781_inline_projection_foreign_key_constraint_failure : BugIntegrationContext
    {
        [Fact]
        public async Task should_be_able_to_create_and_delete_aggregates_with_foreign_keys()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString;
                x.Projections.SelfAggregate<DataImportAggregate>(ProjectionLifecycle.Inline);
                x.Projections.SelfAggregate<DataItemAggregate>(ProjectionLifecycle.Inline)
                    .ForeignKey<DataImportAggregate>(y => y.ImportId);
            });

            await RunTest(documentStore);
        }

        [Fact]
        public async Task order_of_projection_configuration_should_not_matter()
        {
            using var documentStore = SeparateStore(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString;
                x.Projections.SelfAggregate<DataItemAggregate>(ProjectionLifecycle.Inline)
                    .ForeignKey<DataImportAggregate>(y => y.ImportId);
                x.Projections.SelfAggregate<DataImportAggregate>(ProjectionLifecycle.Inline);
            });

            await RunTest(documentStore);
        }

        private async Task RunTest(IDocumentStore documentStore)
        {
            await documentStore.Advanced.Clean.CompletelyRemoveAllAsync();

            var createdByUser = Guid.NewGuid();
            const string importStreamKey = "original";
            const string dataItemStreamKey = "other";

            await using (var session = await documentStore.LightweightSessionAsync())
            {
                session.Events.StartStream<DataImportAggregate>(importStreamKey, new DataImportStartedEvent {ByUser = createdByUser});
                session.Events.StartStream<DataItemAggregate>(dataItemStreamKey, new CreateDataItemEvent {ImportId = importStreamKey, Name = "Data item"});
                await session.SaveChangesAsync();
            }

            await using (var session = await documentStore.QuerySessionAsync())
            {
                var importAggregate = await session.LoadAsync<DataImportAggregate>(importStreamKey);
                importAggregate.ShouldNotBeNull();
                importAggregate.StartedByUser.ShouldBe(createdByUser);

                var dataItemAggregate = await session.LoadAsync<DataItemAggregate>(dataItemStreamKey);
                dataItemAggregate.ShouldNotBeNull();
                dataItemAggregate.ImportId.ShouldBe(importStreamKey);
            }

            await using (var session = await documentStore.LightweightSessionAsync())
            {
                session.Events.Append(dataItemStreamKey, new DeleteDataItemEvent());
                session.Events.Append(importStreamKey, new DeleteImportEvent());
                await session.SaveChangesAsync();
            }

            await using (var session = await documentStore.QuerySessionAsync())
            {
                var importAggregate = await session.LoadAsync<DataImportAggregate>(importStreamKey);
                importAggregate.ShouldBeNull();

                var dataItemAggregate = await session.LoadAsync<DataItemAggregate>(dataItemStreamKey);
                dataItemAggregate.ShouldBeNull();
            }
        }
    }
}

namespace Bug1781
{
    public class DataImportStartedEvent
    {
        public Guid ByUser { get; set; }
    }

    public class DeleteImportEvent
    {
    }

    public class CreateDataItemEvent
    {
        public string ImportId { get; set; }
        public string Name { get; set; }
    }

    public class DeleteDataItemEvent
    {
    }

    public class DataImportAggregate
    {
        public string Id { get; set; }
        public Guid StartedByUser { get; set; }

        public void Apply(DataImportStartedEvent started) => StartedByUser = started.ByUser;

        public bool ShouldDelete(DeleteImportEvent _) => true;
    }

    public class DataItemAggregate
    {
        public string Id { get; set; }

        public string ImportId { get; set; }
        public string Name { get; set; }

        public void Apply(CreateDataItemEvent dataItemEvent)
        {
            ImportId = dataItemEvent.ImportId;
            Name = dataItemEvent.Name;
        }

        public bool ShouldDelete(DeleteDataItemEvent _) => true;
    }
}
