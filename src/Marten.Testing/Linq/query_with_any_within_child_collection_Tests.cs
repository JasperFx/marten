using System;
using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_any_within_child_collection_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        private Target[] targets;

        public query_with_any_within_child_collection_Tests()
        {
            targets = Target.GenerateRandomData(20).ToArray();
            targets.SelectMany(x => x.Children).Each(x => x.Number = 5);

            targets[5].Children[0].Number = 6;
            targets[9].Children[0].Number = 6;
            targets[12].Children[0].Number = 6;

            targets[5].Children[0].Double = -1;
            targets[9].Children[0].Double = -1;
            targets[12].Children[0].Double = 10;

            var child = targets[10].Children[0];
            child.Inner = new Target
            {
                Number = -2
            };

            targets.Each(x => theSession.Store(x));
            theSession.SaveChanges();
        }

        [Fact]
        public void can_query_with_containment_operator()
        {
            theSession.Query<Target>("where data @> '{\"Children\": [{\"Number\": 6}]}'")
                .ToArray()
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(targets[5].Id, targets[9].Id, targets[12].Id);
        }

        [Fact]
        public void can_query_with_an_any_operator()
        {
            // SAMPLE: any-query-through-child-collections
            var results = theSession.Query<Target>()
                .Where(x => x.Children.Any(_ => _.Number == 6))
                .ToArray();
            // ENDSAMPLE

            results
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(targets[5].Id, targets[9].Id, targets[12].Id);
        }

        [Fact]
        public void can_query_with_an_any_operator_that_does_a_multiple_search_within_the_collection()
        {
            // SAMPLE: any-query-through-child-collection-with-and
            var results = theSession
                .Query<Target>()
                .Where(x => x.Children.Any(_ => _.Number == 6 && _.Double == -1))
                .ToArray();
            // ENDSAMPLE

            results
                .Select(x => x.Id)
                .ShouldHaveTheSameElementsAs(targets[5].Id, targets[9].Id);
        }

        [Fact]
        public void can_query_on_deep_properties()
        {
            theSession.Query<Target>().Where(x => x.Children.Any(_ => _.Inner.Number == -2))
                .Single()
                .Id.ShouldBe(targets[10].Id);
        }
    }
}