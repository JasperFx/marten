using System;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Documents;
using Marten.Testing.Events.V4Concepts.Aggregations;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Sdk;

namespace Marten.Testing.Events.Projections
{
    [Collection("self_aggregates")]
    public class self_aggregates : OneOffConfigurationsContext
    {
        public self_aggregates() : base("self_aggregates")
        {

        }

        [Fact]
        public async Task create_with_static_create_method()
        {
            var user = new User {UserName = "jamesworthy"};
            theSession.Store(user);
            await theSession.SaveChangesAsync();

            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new UserStarted {UserId = user.Id});
            await theSession.SaveChangesAsync();

            var query = theStore.QuerySession();
            var user2 = await query.LoadAsync<User>(user.Id);
            user2.ShouldNotBeNull();

            var aggregate = await theSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
            aggregate.UserName.ShouldBe(user.UserName);
        }

        [Fact]
        public async Task create_with_private_constructor()
        {
            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new AEvent());
            await theSession.SaveChangesAsync();

            var aggregate = await theSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
            aggregate.A.ShouldBe(1);
        }

        [Fact]
        public async Task create_with_event_constructor()
        {
            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new BEvent());
            await theSession.SaveChangesAsync();

            var aggregate = await theSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
            aggregate.B.ShouldBe(1);
        }

        [Fact]
        public async Task use_immutable_apply()
        {
            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new BEvent(), new AEvent(), new AEvent());
            await theSession.SaveChangesAsync();

            var aggregate = await theSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
            aggregate.B.ShouldBe(1);
            aggregate.A.ShouldBe(2);
        }

        [Fact]
        public async Task stream_id_is_set()
        {
            var stream = Guid.NewGuid();
            theSession.Events.StartStream(stream, new BEvent(), new AEvent(), new AEvent());
            await theSession.SaveChangesAsync();

            var aggregate = await theSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
            aggregate.Id.ShouldBe(stream);
        }

        [Fact]
        public async Task stream_id_is_set_as_string()
        {
            StoreOptions(x =>
            {
                x.Events.StreamIdentity = StreamIdentity.AsString;
                x.Schema.For<SpecialUsages>().Identity(x => x.Key);
            });

            var stream = Guid.NewGuid().ToString();
            theSession.Events.StartStream(stream, new BEvent(), new AEvent(), new AEvent());
            await theSession.SaveChangesAsync();

            var aggregate = await theSession.Events.AggregateStreamAsync<SpecialUsages>(stream);
            aggregate.Key.ShouldBe(stream);
        }
    }

    public class SpecialUsages
    {
        private SpecialUsages()
        {

        }

        public string Key { get; private set; }

        public Guid Id { get; private set; }

        public SpecialUsages(BEvent @event)
        {
            B = 1;
        }

        public int B { get; private set; }

        public static async Task<SpecialUsages> Create(UserStarted started, IQuerySession session)
        {
            var user = await session.LoadAsync<User>(started.UserId);
            return new SpecialUsages {UserName = user?.UserName};
        }

        public string UserName { get; private set; }

        public SpecialUsages Apply(AEvent @event)
        {
            return new SpecialUsages {A = ++A, UserName = UserName, B = B, Id = Id, Key = Key};
        }

        public int A { get; private set; }


    }
}
