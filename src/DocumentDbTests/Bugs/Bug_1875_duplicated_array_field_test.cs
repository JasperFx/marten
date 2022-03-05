using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_1875_duplicated_array_field_test : BugIntegrationContext
    {
        [Fact]
        public void query_on_duplicated_array_field_test()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(t => t.NumberArray, "int[]");
            });

            using (var session = theStore.OpenSession())
            {
                session.Store(new Target
                {
                    NumberArray = new []{ 1, 2 }
                });

                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                session.Query<Target>().Single(x => x.NumberArray.Contains(1))
                    .NumberArray[0].ShouldBe(1);
            }
        }
    }
}
