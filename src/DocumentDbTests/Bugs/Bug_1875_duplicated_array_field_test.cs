using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1875_duplicated_array_field_test : BugIntegrationContext
{
    [Fact]
    public void query_on_duplicated_number_array_field_test()
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

    [Fact]
    public void query_on_duplicated_guid_array_field_test()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(t => t.GuidArray, "uuid[]");
        });

        var target = new Target {GuidArray = new Guid[] {Guid.NewGuid(), Guid.NewGuid()}};

        using (var session = theStore.OpenSession())
        {
            session.Store(target);
            session.SaveChanges();
        }

        using (var session = theStore.OpenSession())
        {
            session.Query<Target>().Single(x => x.GuidArray.Contains(target.GuidArray[0]))
                .GuidArray[0].ShouldBe(target.GuidArray[0]);
        }
    }
}