using System;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Services.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class CustomAggregatorLookupTests: IntegrationContext
    {
        public CustomAggregatorLookupTests(DefaultStoreFixture fixture) : base(fixture)
        {
            StoreOptions(options =>
            {
                // SAMPLE: scenarios-immutableprojections-storesetup
                var serializer = new JsonNetSerializer();
                serializer.Customize(c => c.ContractResolver = new ResolvePrivateSetters());
                options.Serializer(serializer);
                options.Events.UseAggregatorLookup(AggregationLookupStrategy.UsePrivateApply);
                options.Events.V4Projections.InlineSelfAggregate<AggregateWithPrivateEventApply>();
                // ENDSAMPLE
            });
        }

        [Fact]
        public void can_lookup_private_apply_methods()
        {
            var theGraph = new EventGraph(new StoreOptions());
            theGraph.UseAggregatorLookup(new AggregatorLookup(type => typeof(AggregatorApplyPrivate<>).CloseAndBuildAs<IAggregator>(type)));

            var aggregator = theGraph.AggregateFor<AggregateWithPrivateEventApply>();

            var stream = StreamAction.Append(Guid.NewGuid(), new[] {new QuestStarted {Name = "Destroy the Ring"}});

            var party = aggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");
        }

        [Fact]
        public void can_set_private_apply_aggregator_through_extension_methods_and_strategy()
        {
            var theGraph = new EventGraph(new StoreOptions());
            // SAMPLE: register-custom-aggregator-lookup
            // Registering an aggregator lookup that provides aggregator supporting private Apply([Event Type]) methods
            theGraph.UseAggregatorLookup(AggregationLookupStrategy.UsePrivateApply);
            // ENDSAMPLE

            var aggregator = theGraph.AggregateFor<AggregateWithPrivateEventApply>();

            var stream = StreamAction.Append(Guid.NewGuid(), new object[]{new QuestStarted { Name = "Destroy the Ring" }});

            var party = aggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");
        }

        [Fact]
        public void can_set_public_and_private_apply_aggregator_through_extension_methods_and_strategy()
        {
            var theGraph = new EventGraph(new StoreOptions());
            theGraph.UseAggregatorLookup(AggregationLookupStrategy.UsePublicAndPrivateApply);

            var aggregator = theGraph.AggregateFor<AggregateWithPrivateEventApply>();

            var stream = StreamAction.Append(Guid.NewGuid(), new object[]{new QuestStarted { Name = "Destroy the Ring" }});

            var party = aggregator.Build(stream.Events, null);
            party.Name.ShouldBe("Destroy the Ring");

            stream.Add(new QuestEnded { Name = "Ring Destroyed" });
            var party2 = aggregator.Build(stream.Events, null);
            party2.Name.ShouldBe("Ring Destroyed");
        }

        [Fact]
        public void can_set_aggregator_through_extension_methods_and_strategy()
        {
            var theGraph = new EventGraph(new StoreOptions());
            theGraph.UseAggregatorLookup(AggregationLookupStrategy.UsePublicApply);

            var aggregator = theGraph.AggregateFor<QuestParty>();

            var stream = StreamAction.Start(Guid.NewGuid(), new object[]{new QuestStarted { Name = "Destroy the Ring" }});

            var party = aggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");
        }

        [Fact]
        public void can_use_custom_aggregator_with_inline_projection()
        {
            // SAMPLE: scenarios-immutableprojections-projectstream
            var quest = new QuestStarted { Name = "Destroy the Ring" };
            var questId = Guid.NewGuid();
            theSession.Events.StartStream<QuestParty>(questId, quest);
            theSession.SaveChanges();

            var projection = theSession.Load<AggregateWithPrivateEventApply>(questId);
            projection.Name.ShouldBe("Destroy the Ring");
            // ENDSAMPLE

            SpecificationExtensions.ShouldNotBeNull(theSession.Events.FetchStreamState(questId));
        }
    }

    // SAMPLE: scenarios-immutableprojections-projection
    public class AggregateWithPrivateEventApply
    {
        public Guid Id { get; private set; }

        private void Apply(QuestStarted started)
        {
            Name = started.Name;
        }

        public void Apply(QuestEnded ended)
        {
            Name = ended.Name;
        }

        public string Name { get; private set; }
    }

    // ENDSAMPLE

    // SAMPLE: scenarios-immutableprojections-serializer
    internal class ResolvePrivateSetters: DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(
            MemberInfo member,
            MemberSerialization memberSerialization)
        {
            var prop = base.CreateProperty(member, memberSerialization);

            if (!prop.Writable)
            {
                var property = member as PropertyInfo;
                if (property != null)
                {
                    var hasPrivateSetter = property.GetSetMethod(true) != null;
                    prop.Writable = hasPrivateSetter;
                }
            }

            return prop;
        }
    }

    // ENDSAMPLE
}
