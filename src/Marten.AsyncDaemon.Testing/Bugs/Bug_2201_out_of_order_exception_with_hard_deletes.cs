using System;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Bugs
{
    public class Bug_2201_out_of_order_exception_with_hard_deletes: BugIntegrationContext
    {
        [Fact]
        public async Task should_complete_projection_when_only_hard_deletes_exist_in_batch()
        {
            StoreOptions(options =>
            {
                options.Policies.AllDocumentsAreMultiTenanted();
                options.Events.TenancyStyle = TenancyStyle.Conjoined;
                options.Events.StreamIdentity = StreamIdentity.AsGuid;
                options.Events.MetadataConfig.EnableAll();

                options.Projections.Add<TicketProjection>(ProjectionLifecycle.Async);
            });

            using var daemon = await theStore.BuildProjectionDaemonAsync();
            await daemon.StartAllShards();

            var tenantId = Guid.NewGuid().ToString();
            var ticketId = Guid.NewGuid();
            await using var session = theStore.OpenSession(tenantId);
            for (var i = 1; i <= 1000; i++)
            {
                ticketId = Guid.NewGuid();
                session.Events.Append(ticketId, new TicketCreated(ticketId, $"Ticket #{i}"));
            }

            await session.SaveChangesAsync();
            await daemon.WaitForNonStaleData(5.Seconds());

            session.Events.Append(ticketId, new TicketDeleted(ticketId));
            await session.SaveChangesAsync();
            await daemon.WaitForNonStaleData(5.Seconds());

            var ticket = await session.LoadAsync<Ticket>(ticketId);
            ticket.ShouldBeNull();
        }

        public record TicketCreated(Guid TicketId, string Name);

        public record TicketDeleted(Guid TicketId);

        public class Ticket
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        public class TicketProjection: AggregateProjection<Ticket>
        {
            public TicketProjection()
            {
                Lifecycle = ProjectionLifecycle.Async;
                DeleteEvent<TicketDeleted>();
            }

            public Ticket Create(TicketCreated created) =>
                new() { Id = created.TicketId, Name = created.Name };
        }
    }
}
