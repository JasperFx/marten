using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq.Parsing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;
using Shouldly;

namespace Marten.Testing.Bugs
{
    public class Bug_1217_order_by_count_of_sub_collection : IntegrationContext
    {
        public Bug_1217_order_by_count_of_sub_collection(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public async Task can_order_by_array_length()
        {
            var targets = Target.GenerateRandomData(100).ToArray();
            theStore.BulkInsert(targets);

            Expression<Func<Target, int>> expression = x => x.Children.Length;
            var memberInfos = FindMembers.Determine(expression.Body);
            memberInfos.Length.ShouldBe(2);

            (await theSession.Query<Target>().OrderBy(x => x.Children.Length).ToListAsync()).ShouldNotBeNull();
        }

        public class Root
        {
            public Guid Id { get; set; }
            public string Name { get; set; }

            public ICollection<ChildLevel1> ChildsLevel1 { get; set; }
        }

        public class ChildLevel1
        {
            public Guid Id { get; set; }
            public string Name { get; set; }

            public ICollection<ChildLevel2> ChildsLevel2 { get; set; }
        }

        public class ChildLevel2
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
        }

        [Fact]
        public async Task query_by_list_sub_collection_count()
        {
            // Just a smoke test here
            var list = await theSession.Query<Root>().OrderBy(x => x.ChildsLevel1.Count()).ToListAsync();
            list.ShouldNotBeNull();
        }
    }
}
