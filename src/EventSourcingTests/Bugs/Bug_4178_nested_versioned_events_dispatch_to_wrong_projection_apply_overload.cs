using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs
{
    public class Bug_4178_nested_versioned_events_dispatch_to_wrong_projection_apply_overload: BugIntegrationContext
    {
        [Fact]
        public async Task classic_naming_dispatches_v1_and_v2_to_separate_apply_overloads()
        {
            StoreOptions(opts =>
            {
                opts.Events.EventNamingStyle = EventNamingStyle.ClassicTypeName;
            });

            var streamId = Guid.NewGuid();

            theSession.Events.StartStream<VersionedCustomer>(streamId,
                new VersionedCustomerEvents.V1.CustomerCreated(streamId, "Alice"),
                new VersionedCustomerEvents.V2.CustomerCreated(streamId, "Alice Updated", "alice@example.com"));

            await theSession.SaveChangesAsync();

            var customer = await theSession.Events.AggregateStreamAsync<VersionedCustomer>(streamId);

            customer.ShouldNotBeNull();

            customer.V1ApplyCallCount.ShouldBe(1, "V1 Apply must be called exactly once");
            customer.V2ApplyCallCount.ShouldBe(1, "V2 Apply must be called exactly once");
            customer.Name.ShouldBe("Alice Updated");
            customer.Email.ShouldBe("alice@example.com");
        }

        [Fact]
        public async Task classic_naming_raw_event_types_round_trip_correctly()
        {
            StoreOptions(opts =>
            {
                opts.Events.EventNamingStyle = EventNamingStyle.ClassicTypeName;
            });

            var streamId = Guid.NewGuid();

            theSession.Events.StartStream(streamId,
                new VersionedCustomerEvents.V1.CustomerCreated(streamId, "Alice"),
                new VersionedCustomerEvents.V2.CustomerCreated(streamId, "Bob", "bob@example.com"));
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);

            events.Count.ShouldBe(2);

            events[0].ShouldBeOfType<Event<VersionedCustomerEvents.V1.CustomerCreated>>(
                "first event must deserialise as V1.CustomerCreated");
            events[1].ShouldBeOfType<Event<VersionedCustomerEvents.V2.CustomerCreated>>(
                "second event must deserialise as V2.CustomerCreated");

            var v1Data = ((Event<VersionedCustomerEvents.V1.CustomerCreated>)events[0]).Data;
            v1Data.Name.ShouldBe("Alice");

            var v2Data = ((Event<VersionedCustomerEvents.V2.CustomerCreated>)events[1]).Data;
            v2Data.Name.ShouldBe("Bob");
            v2Data.Email.ShouldBe("bob@example.com");
        }

        [Theory]
        [InlineData(EventNamingStyle.SmarterTypeName)]
        [InlineData(EventNamingStyle.FullTypeName)]
        public async Task smarter_and_full_naming_also_dispatch_correctly(EventNamingStyle namingStyle)
        {
            StoreOptions(opts =>
            {
                opts.Events.EventNamingStyle = namingStyle;
            });

            var streamId = Guid.NewGuid();

            theSession.Events.StartStream<VersionedCustomer>(streamId,
                new VersionedCustomerEvents.V1.CustomerCreated(streamId, "Alice"),
                new VersionedCustomerEvents.V2.CustomerCreated(streamId, "Alice Updated", "alice@example.com"));
            await theSession.SaveChangesAsync();

            var customer = await theSession.Events.AggregateStreamAsync<VersionedCustomer>(streamId);

            customer.ShouldNotBeNull();
            customer.V1ApplyCallCount.ShouldBe(1);
            customer.V2ApplyCallCount.ShouldBe(1);
            customer.Email.ShouldBe("alice@example.com");
        }
    }

    public static class VersionedCustomerEvents
    {
        public static class V1
        {
            public record CustomerCreated(Guid Id, string Name);
        }

        public static class V2
        {
            public record CustomerCreated(Guid Id, string Name, string Email);
        }
    }

    public class VersionedCustomer
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }

        public int V1ApplyCallCount { get; set; }

        public int V2ApplyCallCount { get; set; }

        public void Apply(IEvent<VersionedCustomerEvents.V1.CustomerCreated> e)
        {
            Id = e.Data.Id;
            Name = e.Data.Name;
            V1ApplyCallCount++;
        }

        public void Apply(IEvent<VersionedCustomerEvents.V2.CustomerCreated> e)
        {
            Id = e.Data.Id;
            Name = e.Data.Name;
            Email = e.Data.Email;
            V2ApplyCallCount++;
        }
    }
}
