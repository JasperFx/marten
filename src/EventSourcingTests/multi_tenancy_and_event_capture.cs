using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests
{
    public class multi_tenancy_and_event_capture: OneOffConfigurationsContext
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
            StoreOptions(_ =>
            {
                _.Events.TenancyStyle = tenancyStyle;
                _.Events.StreamIdentity = streamIdentity;
                _.Policies.AllDocumentsAreMultiTenanted();
            }, true);
        }

        public multi_tenancy_and_event_capture(ITestOutputHelper output)
        {
            _output = output;
        }

        public static TheoryData<StreamIdentity, Func<DocumentStore, IDocumentSession>, Action<IDocumentSession>, Action<IDocumentSession>> WillParameterizeTenantId => new()
        {
            {
                StreamIdentity.AsGuid,
                s => s.OpenSession(),
                s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
                s => { s.Events.Append(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); }
            },
            {
                StreamIdentity.AsGuid,
                s => s.OpenSession("Green"),
                s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
                s => { s.Events.Append(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); }
            },
            {
                StreamIdentity.AsGuid,
                s => s.OpenSession(),
                s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
                s => { s.Events.AppendOptimistic(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsGuid,
                s => s.OpenSession("Green"),
                s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
                s => { s.Events.AppendOptimistic(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsGuid,
                s => s.OpenSession(),
                s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
                s => { s.Events.AppendExclusive(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsGuid,
                s => s.OpenSession("Green"),
                s => { s.Events.StartStream(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()); },
                s => { s.Events.AppendExclusive(Guid.Parse("0b60936d-1be0-4378-8e4c-275263e123d1"), new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsString,
                s => s.OpenSession(),
                s => { s.Events.StartStream("Stream", new MembersJoined()); },
                s => { s.Events.Append("Stream", new MembersJoined()); }
            },
            {
                StreamIdentity.AsString,
                s => s.OpenSession("Green"),
                s => { s.Events.StartStream("Stream", new MembersJoined()); },
                s => { s.Events.Append("Stream", new MembersJoined()); }
            },
            {
                StreamIdentity.AsString,
                s => s.OpenSession(),
                s => { s.Events.StartStream("Stream", new MembersJoined()); },
                s => { s.Events.AppendOptimistic("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsString,
                s => s.OpenSession("Green"),
                s => { s.Events.StartStream("Stream", new MembersJoined()); },
                s => { s.Events.AppendOptimistic("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsString,
                s => s.OpenSession(),
                s => { s.Events.StartStream("Stream", new MembersJoined()); },
                s => { s.Events.AppendExclusive("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
            },
            {
                StreamIdentity.AsString,
                s => s.OpenSession("Green"),
                s => { s.Events.StartStream("Stream", new MembersJoined()); },
                s => { s.Events.AppendExclusive("Stream", new MembersJoined()).GetAwaiter().GetResult(); }
            },
        };

        [Theory]
        [MemberData(nameof(WillParameterizeTenantId))]
        public void will_parameterize_tenant_id_when_checking_stream_version(StreamIdentity streamIdentity, Func<DocumentStore, IDocumentSession> openSession, Action<IDocumentSession> startStream, Action<IDocumentSession> append)
        {
            InitStore(TenancyStyle.Conjoined, streamIdentity);
            theStore.Advanced.Clean.DeleteAllEventData();

            var streamId = Guid.NewGuid();
            using (var session = openSession(theStore))
            {
                startStream(session);
                session.SaveChanges();
            }

            using (var session = openSession(theStore))
            {
                append(session);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Red"))
            {
                startStream(session);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession("Red"))
            {
                append(session);
                session.SaveChanges();
            }
        }
    }
}
