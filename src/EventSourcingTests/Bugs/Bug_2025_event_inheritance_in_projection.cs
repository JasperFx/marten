using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace EventSourcingTests.Bugs;

public class Bug_2025_event_inheritance_in_projection : IntegrationContext
{
    public Bug_2025_event_inheritance_in_projection(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Create_And_Read_User_FromEventStream()
    {
        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Identity));
        await theStore.Advanced.Clean.DeleteAllEventDataAsync();

        var @created = new UserCreated(Guid.NewGuid(), "google-some-name-identifier", "Nancy", "Drew");
        theSession.Events.StartStream(@created.Id, created);
        await theSession.SaveChangesAsync();

        var user = await theSession.Events.AggregateStreamAsync<Identity>(@created.Id);
        user.FirstName.ShouldBe("Nancy");
    }
    public record IdentityAdded(string NameIdentifier, string FirstName, string LastName);
    public record UserCreated(Guid Id, string NameIdentifier, string FirstName, string LastName) : IdentityAdded(NameIdentifier, FirstName, LastName);
    public record UserDeleted(Guid Id);

    public class Identity
    {

        public Identity(UserCreated @event)
        {
            Id = @event.Id;
            Apply(@event);
        }

        public Guid Id { get; private set; }
        public string LastName { get; private set; }
        public string FirstName { get; private set; }

        public void Apply(IdentityAdded @event)
        {
            ArgumentNullException.ThrowIfNull(@event);

            LastName = @event.LastName;
            FirstName = @event.FirstName;
        }
    }

}
