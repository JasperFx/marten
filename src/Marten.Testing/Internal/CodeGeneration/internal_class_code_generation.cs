using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

[assembly: InternalsVisibleTo("Marten.Generated")]

namespace Marten.Testing.Internal.CodeGeneration
{
    internal class InternalEvent
    {
        public Guid AggregateId { get; }
        public string Name { get; }

        public InternalEvent(Guid aggregateId, string name)
        {
            AggregateId = aggregateId;
            Name = name;
        }
    }

    internal class InternalAggregate
    {
        public Guid Id { get; private set; }
        public string Name { get; private set; }

        public void Apply(InternalEvent @event)
        {
            Id = @event.AggregateId;
            Name = @event.Name;
        }
    }

    public class internal_class_code_generation: IntegrationContext
    {
        public internal_class_code_generation(DefaultStoreFixture fixture): base(fixture)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.InlineProjections.AggregateStreamsWith<InternalAggregate>();
            });
        }

        [Fact]
        public async Task can_store_and_load_inline_projection()
        {
            var streamId = Guid.NewGuid();
            const string name = "test";

            await using (var session = theStore.OpenSession())
            {
                session.Events.Append(streamId, new InternalEvent(streamId, name));

                await session.SaveChangesAsync();
            }

            await using (var session = theStore.OpenSession())
            {
                var aggregate = await session.LoadAsync<InternalAggregate>(streamId);

                aggregate.Id.ShouldBe(streamId);
                aggregate.Name.ShouldBe(name);

                var @event = await session.Events.QueryRawEventDataOnly<InternalEvent>()
                    .Where(e => e.AggregateId == streamId)
                    .SingleOrDefaultAsync();

                @event.AggregateId.ShouldBe(streamId);
                @event.Name.ShouldBe(name);
            }
        }
    }
}
