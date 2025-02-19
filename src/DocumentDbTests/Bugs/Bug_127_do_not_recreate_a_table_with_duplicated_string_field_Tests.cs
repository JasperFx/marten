using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_127_do_not_recreate_a_table_with_duplicated_string_field_Tests : BugIntegrationContext
{
    [Fact]
    public async Task does_not_recreate_the_table()
    {
        var store1 = SeparateStore(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
        });

        await store1.Advanced.Clean.CompletelyRemoveAllAsync();

        using (var session1 = store1.LightweightSession())
        {
            session1.Store(new Team { Name = "Warriors" });
            session1.Store(new Team { Name = "Spurs" });
            session1.Store(new Team { Name = "Thunder" });

            await session1.SaveChangesAsync();

            session1.Query<Team>().Count().ShouldBe(3);
        }

        var store2 = SeparateStore(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.All;
        });

        using (var session2 = store2.QuerySession())
        {
            session2.Query<Team>().Count().ShouldBe(3);
        }
    }
}

public class Team
{
    public Guid Id { get; set; }

    [DuplicateField]
    public string Name { get; set; }
}
