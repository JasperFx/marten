using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections;
using Marten;
using Marten.Services.Json;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests
{
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
        public async Task check_user_defined_metadata_not_exists()
        {
            StoreOptions(_ => _.Events.MetadataConfig.HeadersEnabled = true);
            const string userDefinedMetadataName = "my-custom-metadata";

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

        #region sample_event_metadata_overrides

        [Fact]
        public async Task check_event_metadata_overrides()
        {
            StoreOptions(_ => _.Events.MetadataConfig.EnableAll());

            const string correlationId = "test-correlation-id";
            theSession.CorrelationId = correlationId;

            const string causationId = "test-causation-id";
            theSession.CausationId = causationId;

            const string userDefinedMetadata1Name = "my-header-1";
            const string userDefinedMetadata1Value = "my-header-1-value";
            theSession.SetHeader(userDefinedMetadata1Name, userDefinedMetadata1Value);
            const string userDefinedMetadata2Name = "my-header-2";
            const string userDefinedMetadata2Value = "my-header-2-value";
            theSession.SetHeader(userDefinedMetadata2Name, userDefinedMetadata2Value);


            // override the correlation ids
            const string correlationIdOverride = "override-correlation-id";
            theSession.Events.ApplyCorrelationId(correlationIdOverride, started, joined);

            // override the causation ids
            const string causationIdOverride = "override-causation-id";
            theSession.Events.ApplyCausationId(causationIdOverride, started, joined);

            // update an existing header on one event
            const string overrideMetadata1Value = "my-header-1-override-value";
            theSession.Events.ApplyHeader(userDefinedMetadata1Name, overrideMetadata1Value, started);

            // add a new header on one event
            const string overrideMetadata3Name = "my-header-override";
            const string overrideMetadata3Value = "my-header-override-value";
            theSession.Events.ApplyHeader(overrideMetadata3Name, overrideMetadata3Value, slayed);

            // actually add the events to the session
            // this can be done before or after metadata overrides are applied
            var streamId = theSession.Events
                .StartStream<QuestParty>(started, joined, slayed).Id;
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);

            events[0].CorrelationId.ShouldBe(correlationIdOverride);
            events[1].CorrelationId.ShouldBe(correlationIdOverride);
            events[2].CorrelationId.ShouldBe(correlationId);

            events[0].CausationId.ShouldBe(causationIdOverride);
            events[1].CausationId.ShouldBe(causationIdOverride);
            events[2].CausationId.ShouldBe(causationId);

            events[0].GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(overrideMetadata1Value);
            events[0].GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
            events[0].GetHeader(overrideMetadata3Name).ShouldBeNull();

            events[1].GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(userDefinedMetadata1Value);
            events[1].GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
            events[1].GetHeader(overrideMetadata3Name).ShouldBeNull();

            events[2].GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(userDefinedMetadata1Value);
            events[2].GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
            events[2].GetHeader(overrideMetadata3Name).ToString().ShouldBe(overrideMetadata3Value);
        }

        #endregion

        [Fact]
        public async Task check_copy_metadata_from_existing_record()
        {
            StoreOptions(_ =>
            {
                _.Events.MetadataConfig.EnableAll();
                _.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
            });

            const string correlationId = "test-correlation-id";
            theSession.CorrelationId = correlationId;

            const string causationId = "test-causation-id";
            theSession.CausationId = causationId;

            const string userDefinedMetadata1Name = "my-header-1";
            const string userDefinedMetadata1Value = "my-header-1-value";
            theSession.SetHeader(userDefinedMetadata1Name, userDefinedMetadata1Value);
            const string userDefinedMetadata2Name = "my-header-2";
            const string userDefinedMetadata2Value = "my-header-2-value";
            theSession.SetHeader(userDefinedMetadata2Name, userDefinedMetadata2Value);

            var streamId = theSession.Events
                .StartStream<QuestParty>(started).Id;
            await theSession.SaveChangesAsync();
            
            // reset manually because session wont clear metadata unless disposed
            theSession.CorrelationId = null;
            theSession.CausationId = null;
            theSession.SetHeader(userDefinedMetadata1Name, null);
            theSession.SetHeader(userDefinedMetadata2Name, null);

            var writtenStarted = (await theSession.Events.FetchStreamAsync(streamId))[0];

            theSession.Events.CopyMetadata(writtenStarted, slayed);
            theSession.Events.Append(streamId, joined, slayed);
            await theSession.SaveChangesAsync();

            var events = await theSession.Events.FetchStreamAsync(streamId);
            var writtenJoined = events[1];
            var writtenSlayed = events[2];

            writtenJoined.CorrelationId.ShouldBeNull();
            writtenJoined.CausationId.ShouldBeNull();
            writtenJoined.GetHeader(userDefinedMetadata1Name).ShouldBeNull();
            writtenJoined.GetHeader(userDefinedMetadata2Name).ShouldBeNull();

            writtenSlayed.CorrelationId.ShouldBe(correlationId);
            writtenSlayed.CausationId.ShouldBe(causationId);
            writtenSlayed.GetHeader(userDefinedMetadata1Name).ToString().ShouldBe(userDefinedMetadata1Value);
            writtenSlayed.GetHeader(userDefinedMetadata2Name).ToString().ShouldBe(userDefinedMetadata2Value);
        }

        [Fact]
        public async Task check_writing_empty_headers_system_text_json()
        {
            StoreOptions(_ =>
            {
                _.Events.MetadataConfig.EnableAll();
                _.UseDefaultSerialization(serializerType: SerializerType.SystemTextJson);
            });

            var streamId = theSession.Events
                .StartStream<QuestParty>(started).Id;
            await theSession.SaveChangesAsync();
            // Should not throw System.NullReferenceException here
        }


        [Fact]
        public async Task check_writing_empty_headers_newtonsoft_json()
        {
            StoreOptions(_ =>
            {
                _.Events.MetadataConfig.EnableAll();
                _.UseDefaultSerialization(serializerType: SerializerType.Newtonsoft);
            });

            var streamId = theSession.Events
                .StartStream<QuestParty>(started).Id;
            await theSession.SaveChangesAsync();
            // Should not throw System.NullReferenceException here
        }
    }
}
