using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class multi_tenancy_and_event_capture : IntegratedFixture
    {
        public static TheoryData<TenancyStyle> TenancyStyles = new TheoryData<TenancyStyle>
        {
            { TenancyStyle.Conjoined },
            { TenancyStyle.Single },
        };

        [Theory]
        [MemberData("TenancyStyles")]
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
        [MemberData("TenancyStyles")]
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
        [MemberData("TenancyStyles")]
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
        [MemberData("TenancyStyles")]
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
        [MemberData("TenancyStyles")]
        public void append_to_events_a_second_time_with_same_tenant_id(TenancyStyle tenancyStyle)
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
        public void try_to_append_across_tenants_with_tenancy_style_single()
        {
            InitStore(TenancyStyle.Single);

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
    }
}