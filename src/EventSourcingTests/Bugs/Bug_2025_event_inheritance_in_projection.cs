using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs
{
    public class Bug_2025_event_inheritance_in_projection : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_2025_event_inheritance_in_projection(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }

        [Fact]
        public async Task Create_And_Read_User_FromEventStream()
        {
            await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(Identity));
            await theStore.Advanced.Clean.DeleteAllEventDataAsync();

            theSession.Logger = new TestOutputMartenLogger(_output);

            var @created = new UserCreated(Guid.NewGuid(), "google-some-name-identifier", "Nancy", "Drew");
            theSession.Events.StartStream(@created.Id, created);
            await theSession.SaveChangesAsync();

            var user = theSession.Events.AggregateStream<Identity>(@created.Id);
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
                if (@event is null) throw new ArgumentNullException();

                LastName = @event.LastName;
                FirstName = @event.FirstName;
            }
        }

    }
}
