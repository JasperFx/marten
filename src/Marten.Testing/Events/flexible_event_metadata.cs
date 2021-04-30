using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("flexible_metadata")]
    public class flexible_event_metadata : OneOffConfigurationsContext
    {
        private QuestStarted started = new QuestStarted { Name = "Find the Orb" };
        private MembersJoined joined = new MembersJoined { Day = 2, Location = "Faldor's Farm", Members = new string[] { "Garion", "Polgara", "Belgarath" } };
        private MonsterSlayed slayed = new MonsterSlayed { Name = "Troll" };

        [Fact]
        public async Task check_metadata_correlation_id_enabled()
        {
            StoreOptions(_ => _.Events.MetadataConfig.CorrelationIdEnabled = true);
            const string correlationId = "test-correlation-id";
            theSession.CorrelationId = correlationId;

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                @event.CorrelationId.ShouldBe(correlationId);
            }
        }

        [Fact]
        public async Task check_search_with_correlation_id()
        {
            StoreOptions(_ => _.Events.MetadataConfig.CorrelationIdEnabled = true);
            const string correlationId = "test-correlation-id";
            theSession.CorrelationId = correlationId;

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.QueryAllRawEvents()
                .Where(x => x.CorrelationId == correlationId)
                .ToListAsync();

            events.Count.ShouldBe(3);
            events[0].StreamId.ShouldBe(streamId);
            events[1].StreamId.ShouldBe(streamId);
            events[2].StreamId.ShouldBe(streamId);
        }

        [Fact]
        public async Task check_metadata_correlation_id_disabled()
        {
            // note: by default CorrelationId meta data is not enabled
            const string correlationId = "test-correlation-id";
            theSession.CorrelationId = correlationId;

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                @event.CorrelationId.ShouldBeNullOrEmpty();
            }
        }

        [Fact]
        public async Task check_metadata_causation_id_enabled()
        {
            StoreOptions(_ => _.Events.MetadataConfig.CausationIdEnabled = true);
            const string causationId = "test-causation-id";
            theSession.CausationId = causationId;

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                @event.CausationId.ShouldBe(causationId);
            }
        }

        [Fact]
        public async Task check_search_with_causation_id()
        {
            StoreOptions(_ => _.Events.MetadataConfig.CausationIdEnabled = true);
            const string causationId = "test-causation-id";
            theSession.CausationId = causationId;

            var streamId = theSession.Events.StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.QueryAllRawEvents()
                .Where(x => x.CausationId == causationId)
                .ToListAsync();

            events.Count.ShouldBe(3);
            events[0].StreamId.ShouldBe(streamId);
            events[1].StreamId.ShouldBe(streamId);
            events[2].StreamId.ShouldBe(streamId);
        }

        [Fact]
        public async Task check_metadata_causation_id_disabled()
        {
            // note: by default CausationId meta data is not enabled
            const string causationId = "test-causation-id";
            theSession.CausationId = causationId;

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                @event.CausationId.ShouldBeNullOrEmpty();
            }
        }

        [Fact]
        public async Task check_user_defined_metadata_enabled()
        {
            StoreOptions(_ => _.Events.MetadataConfig.HeadersEnabled = true);
            const string userDefinedMetadataName = "my-custom-metadata";
            const string userDefinedMetadataValue = "my-custom-metadata-value";
            theSession.SetHeader(userDefinedMetadataName, userDefinedMetadataValue);

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                var retrievedVal = @event.GetHeader(userDefinedMetadataName).ToString();
                retrievedVal.ShouldBe(userDefinedMetadataValue);
            }
        }

        [Fact]
        public async Task check_user_defined_metadata_disabled()
        {
            // note: by default user defined meta data is not enabled
            const string userDefinedMetadataName = "my-custom-metadata";
            const string userDefinedMetadataValue = "my-custom-metadata-value";
            theSession.SetHeader(userDefinedMetadataName, userDefinedMetadataValue);

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                @event.GetHeader(userDefinedMetadataName).ShouldBeNull();
            }
        }

        [Fact]
        public async Task check_flexible_metadata_with_all_enabled()
        {
            StoreOptions(_ => _.Events.MetadataConfig.EnableAll());

            const string correlationId = "test-correlation-id";
            theSession.CorrelationId = correlationId;

            const string causationId = "test-causation-id";
            theSession.CausationId = causationId;

            const string userDefinedMetadataName = "my-custom-metadata";
            const string userDefinedMetadataValue = "my-custom-metadata-value";
            theSession.SetHeader(userDefinedMetadataName, userDefinedMetadataValue);

            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            foreach (var @event in events)
            {
                @event.CorrelationId.ShouldBe(correlationId);
                @event.CausationId.ShouldBe(causationId);

                var retrievedVal = @event.GetHeader(userDefinedMetadataName).ToString();
                retrievedVal.ShouldBe(userDefinedMetadataValue);
            }
        }

        public flexible_event_metadata() : base("event_flex_meta")
        {
        }
    }
}
