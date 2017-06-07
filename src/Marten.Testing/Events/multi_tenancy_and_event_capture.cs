using System;
using System.Threading.Tasks;
using Marten.Events;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class multi_tenancy_and_event_capture : IntegratedFixture
    {
        public multi_tenancy_and_event_capture()
        {
            StoreOptions(_ => _.Policies.AllDocumentsAreMultiTenanted());
        }

        [Fact]
        public void capture_events_for_a_tenant()
        {
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

        [Fact]
        public async Task capture_events_for_a_tenant_async()
        {
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

        [Fact]
        public void capture_events_for_a_tenant_with_string_identifier()
        {
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Events.StreamIdentity = StreamIdentity.AsString;
            });

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

        [Fact]
        public async Task capture_events_for_a_tenant_async_as_string_identifier()
        {
            StoreOptions(_ =>
            {
                _.Policies.AllDocumentsAreMultiTenanted();
                _.Events.StreamIdentity = StreamIdentity.AsString;
            });

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

        [Fact]
        public void append_to_events_a_second_time_with_same_tenant_id()
        {
            Guid stream = Guid.NewGuid();
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

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

        [Fact]
        public void try_to_append_across_tenants()
        {
            Guid stream = Guid.NewGuid();
            using (var session = theStore.OpenSession("Green"))
            {
                session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                session.SaveChanges();
            }

            Exception<MartenCommandException>.ShouldBeThrownBy(() =>
            {
                using (var session = theStore.OpenSession("Red"))
                {
                    session.Events.Append(stream, new MembersJoined(), new MembersJoined());
                    session.SaveChanges();
                }
            }).Message.ShouldContain("The tenantid does not match the existing stream");


        }
    }
}