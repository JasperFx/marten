using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Storage;
using Marten.Testing.Harness;
using Xunit;
using Bug2177;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Shouldly;

namespace Marten.AsyncDaemon.Testing.Bugs
{
    public class Bug_2177_query_session_tenancy_in_daemon: BugIntegrationContext
    {
        [Fact]
        public async Task should_have_tenancy_set_correctly()
        {
            StoreOptions(options =>
            {
                options.Policies.AllDocumentsAreMultiTenanted();
                options.Events.TenancyStyle = TenancyStyle.Conjoined;
                options.Events.StreamIdentity = StreamIdentity.AsGuid;
                options.Events.MetadataConfig.EnableAll();

                options.Projections.Add<TicketProjection>(ProjectionLifecycle.Async);
                options.GeneratedCodeMode = TypeLoadMode.Auto;

                options.Schema.For<User>().MultiTenanted();
            });


            var tenantId = Guid.NewGuid().ToString();
            var userId = Guid.NewGuid();
            var ticketId = Guid.NewGuid();

            await using var session = TheStore.LightweightSession(tenantId);
            session.Insert(new User { Id = userId, FirstName = "Tester", LastName = "McTestFace" });
            await session.SaveChangesAsync();

            await insertUserWithSameIdInOtherTenant(TheStore, userId);

            session.Events.Append(ticketId, new TicketCreated(ticketId, "Test Projections"),
                new TicketAssigned(ticketId, userId));
            await session.SaveChangesAsync();

            using var daemon = await TheStore.BuildProjectionDaemonAsync();
            await daemon.StartAllShards();
            await daemon.WaitForNonStaleData(1.Minutes());

            var projection = await session.LoadAsync<Ticket>(ticketId);
            projection.User.ShouldNotBeNull();
            projection.User.FirstName.ShouldBe("Tester");
        }

        private static async Task insertUserWithSameIdInOtherTenant(DocumentStore documentStore, Guid userId)
        {
            await using var defaultSession = documentStore.LightweightSession();
            defaultSession.Store(new User { Id = userId, FirstName = "Somebody", LastName = "Else" });
            await defaultSession.SaveChangesAsync();
        }
    }
}

namespace Bug2177
{
    public record TicketDeleted(Guid TicketId);

    public record TicketCreated(Guid TicketId, string Name);

    public record TicketAssigned(Guid TicketId, Guid UserId);

    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
    }

    public class Ticket
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public User User { get; set; }
    }

    public class TicketProjection: SingleStreamProjection<Ticket>
    {
        public Ticket Create(TicketCreated created) =>
            new() { Id = created.TicketId, Name = created.Name };

        public void Apply(Ticket ticket, TicketAssigned assigned, IQuerySession session)
        {
            ticket.User = session.Load<User>(assigned.UserId);
        }
    }
}
