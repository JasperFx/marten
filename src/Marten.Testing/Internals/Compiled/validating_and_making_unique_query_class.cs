using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Marten.Internal.CompiledQueries;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Internals.Compiled
{
    public class validating_and_making_unique_query_class
    {
        public class QueryWithLots : ICompiledListQuery<Target>
        {
            public bool Flag { get; set; }
            public string String { get; set; }
            public int Number { get; set; }
            public int? NullableNumber { get; set; }
            public DateTime Date;
            public DateTime? NullableDate;

            public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
            {
                throw new System.NotSupportedException();
            }
        }

        [Fact]
        public void find_members_that_are_not_supported()
        {
            var plan = new CompiledQueryPlan(typeof(QueryWithLots), typeof(Target));

            plan.FindMembers();

            var invalids = plan.InvalidMembers;
            invalids.Select(x => x.Name).OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("Flag", "NullableDate", "NullableNumber");
        }

        [Fact]
        public void find_field_members()
        {
            var plan = new CompiledQueryPlan(typeof(QueryWithLots), typeof(Target));

            plan.FindMembers();

            plan.Parameters.OfType<FieldQueryMember<DateTime>>()
                .Count().ShouldBe(1);
        }

        [Fact]
        public void find_property_members()
        {
            var plan = new CompiledQueryPlan(typeof(QueryWithLots), typeof(Target));

            plan.FindMembers();

            plan.Parameters.OfType<PropertyQueryMember<string>>()
                .Count().ShouldBe(1);

            plan.Parameters.OfType<PropertyQueryMember<int>>()
                .Count().ShouldBe(1);
        }

        public class PagedTargets : ICompiledListQuery<Target>
        {
            public QueryStatistics Statistics { get; } = new QueryStatistics();

            public Expression<Func<IMartenQueryable<Target>, IEnumerable<Target>>> QueryIs()
            {
                throw new System.NotSupportedException();
            }

            public int Page { get; set; }
        }

        [Fact]
        public void find_the_statistics_member()
        {
            var plan = new CompiledQueryPlan(typeof(PagedTargets), typeof(Target));

            plan.FindMembers();

            plan.StatisticsMember.Name.ShouldBe("Statistics");
        }


    }
}
