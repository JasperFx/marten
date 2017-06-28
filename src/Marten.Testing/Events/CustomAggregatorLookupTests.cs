using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Services.Events;
using Marten.Testing.Events.Projections;
using Marten.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    public class CustomAggregatorLookupTests : DocumentSessionFixture<NulloIdentityMap>
    {        
        public CustomAggregatorLookupTests()
        {    
            this.InProfile(TestingContracts.CamelCase, () =>
            {
                StoreOptions(options =>
                {
                    var serializer = new JsonNetSerializer();                    
                    var resolver = new ResolvePrivateSetters {NamingStrategy = new CamelCaseNamingStrategy()};

                    serializer.Customize(c => c.ContractResolver = resolver);
                    options.Serializer(serializer);
                    options.Events.UseAggregatorLookup(AggregationLookupStrategy.UsePrivateApply);
                    options.Events.InlineProjections.AggregateStreamsWith<AggregateWithPrivateEventApply>();                    
                });
            }).Otherwise(() =>
            {
                StoreOptions(options =>
                {
                    // SAMPLE: scenarios-immutableprojections-storesetup
                    var serializer = new JsonNetSerializer();
                    serializer.Customize(c => c.ContractResolver = new ResolvePrivateSetters());
                    options.Serializer(serializer);
                    options.Events.UseAggregatorLookup(AggregationLookupStrategy.UsePrivateApply);
                    options.Events.InlineProjections.AggregateStreamsWith<AggregateWithPrivateEventApply>();
                    // ENDSAMPLE
                });
            });    
        }

        [Fact]
        public void can_lookup_private_apply_methods()
        {
            var theGraph = new EventGraph(new StoreOptions());
            theGraph.UseAggregatorLookup(new AggregatorLookup(type => typeof(AggregatorApplyPrivate<>).CloseAndBuildAs<IAggregator>(type)));            

            var aggregator = theGraph.AggregateFor<AggregateWithPrivateEventApply>();

            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted {Name = "Destroy the Ring"});                

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

            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted { Name = "Destroy the Ring" });

            var party = aggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");
        }

        [Fact]
        public void can_set_aggregator_through_extension_methods_and_strategy()
        {
            var theGraph = new EventGraph(new StoreOptions());
            theGraph.UseAggregatorLookup(AggregationLookupStrategy.UsePublicApply);            

            var aggregator = theGraph.AggregateFor<QuestParty>();

            var stream = new EventStream(Guid.NewGuid(), false)
                .Add(new QuestStarted { Name = "Destroy the Ring" });

            var party = aggregator.Build(stream.Events, null);

            party.Name.ShouldBe("Destroy the Ring");
        }

        [Fact]
        public void can_use_custom_aggregator_with_inline_projection()
        {
            // SAMPLE: scenarios-immutableprojections-projectstream
            var quest = new QuestStarted {Name = "Destroy the Ring"};
            var questId = Guid.NewGuid();
            theSession.Events.StartStream<QuestParty>(questId, quest);
            theSession.SaveChanges();

            var projection = theSession.Load<AggregateWithPrivateEventApply>(questId);
            projection.Name.ShouldBe("Destroy the Ring");
            // ENDSAMPLE

            theSession.Events.FetchStreamState(questId).ShouldNotBeNull();
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

        public string Name { get; private set; }
    }
    // ENDSAMPLE

    // SAMPLE: scenarios-immutableprojections-serializer
    internal class ResolvePrivateSetters : DefaultContractResolver
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