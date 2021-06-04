using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Events
{
    public class multi_tenancy_and_event_capture: IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public static TheoryData<TenancyStyle> TenancyStyles = new TheoryData<TenancyStyle>
        {
            { TenancyStyle.Conjoined },
            { TenancyStyle.Single },
        };

        [Theory]
        [MemberData(nameof(TenancyStyles))]
        public void capture_events_for_a_tenant(TenancyStyle tenancyStyle)
        {
            InitStore(tenancyStyle);

            Guid stream = Guid.NewGuid();
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Green"))
            {
                var events = session.Events.FetchStream(stream);
                foreach (var @event in events)
                {
                    @event.TenantId.ShouldBe("Green");
                }
            }
        }

        [Theory]
        [MemberData(nameof(TenancyStyles))]
        public async Task capture_events_for_a_tenant_async(TenancyStyle tenancyStyle)
        {
            InitStore(tenancyStyle);

            Guid stream = Guid.NewGuid();
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession("Green"))
            {
                var events = await session.Events.FetchStreamAsync(stream);
                foreach (var @event in events)
                {
                    @event.TenantId.ShouldBe("Green");
                }
            }
        }

        [Theory]
        [MemberData(nameof(TenancyStyles))]
        public void capture_events_for_a_tenant_with_string_identifier(TenancyStyle tenancyStyle)
        {
            InitStore(tenancyStyle, StreamIdentity.AsString);

            var stream = "SomeStream";
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Green"))
            {
                var events = session.Events.FetchStream(stream);
                foreach (var @event in events)
                {
                    @event.TenantId.ShouldBe("Green");
                }
            }
        }

        [Theory]
        [MemberData(nameof(TenancyStyles))]
        public async Task capture_events_for_a_tenant_async_as_string_identifier(TenancyStyle tenancyStyle)
        {
            InitStore(tenancyStyle, StreamIdentity.AsString);

            var stream = "SomeStream";
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                await session.SaveChangesAsync();
            }

            using (var session = theStore.OpenSession("Green"))
            {
                var events = await session.Events.FetchStreamAsync(stream);
                foreach (var @event in events)
                {
                    @event.TenantId.ShouldBe("Green");
                }
            }
        }

        [Theory]
        [MemberData(nameof(TenancyStyles))]
        public void append_to_events_a_second_time_with_same_tenant_id(TenancyStyle tenancyStyle)
        {
            InitStore(tenancyStyle);

            Guid stream = Guid.NewGuid();
            using (var session = theStore.OpenSession("Green"))
            {
                session.Logger = new TestOutputMartenLogger(_output);
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Green"))
            {
                session.Logger = new TestOutputMartenLogger(_output);
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Green"))
            {
                var events = session.Events.FetchStream(stream);
                foreach (var @event in events)
                {
                    @event.TenantId.ShouldBe("Green");
                }
            }
        }


        [Fact]
        public void try_to_append_across_tenants_with_tenancy_style_conjoined()
        {
            InitStore(TenancyStyle.Conjoined);

            Guid stream = Guid.NewGuid();
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            Should.NotThrow(() =>
            {
                using (var session = theStore.OpenSession("Red"))
                {
                    session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                    session.SaveChanges();
                }
            });
        }

        [Fact]
        public void tenanted_session_should_not_see_other_tenants_events()
        {
            InitStore(TenancyStyle.Conjoined);

            theStore.Advanced.Clean.DeleteAllEventData();

            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(Guid.NewGuid(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Red"))
            {
                session.Events.Append(Guid.NewGuid(), new MembersJoined());
                session.SaveChanges();
            }

            using (var session = theStore.QuerySession("Green"))
            {
                var memberJoins = session.Query<MembersJoined>().ToList();
                memberJoins.Count.ShouldBe(1);
            }
        }

        private void InitStore(TenancyStyle tenancyStyle, StreamIdentity streamIdentity = StreamIdentity.AsGuid)
        {
            var databaseSchema = $"{GetType().Name}_{tenancyStyle.ToString().ToLower()}";

            StoreOptions(_ =>
            {
                _.Events.DatabaseSchemaName = databaseSchema;
                _.Events.TenancyStyle = tenancyStyle;
                _.Events.StreamIdentity = streamIdentity;
                _.Policies.AllDocumentsAreMultiTenanted();
            });
        }

        public multi_tenancy_and_event_capture(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }
    }
}
