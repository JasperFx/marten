using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_834_querying_inside_of_child_collections : IntegrationContext
    {
        public Bug_834_querying_inside_of_child_collections(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        public class Contact {
            public List<string> Tags {get;set;}
            public Guid Id { get; set; }
        }

        [Fact]
        public void can_query_with_condition_within_any()
        {
            var c1 = new Contact{Tags = new List<string>{"AA", "B", "C"}};
            var c2 = new Contact{Tags = new List<string>{"B", "AC", "D"}};
            var c3 = new Contact{Tags = new List<string>{"H", "I", "J"}};

            theStore.BulkInsert(new Contact[]{c1, c2, c3});

            theSession.Query<Contact>()
                .Count(x => x.Tags.Any(t => t.StartsWith("A"))).ShouldBe(2);
        }
    }
}
