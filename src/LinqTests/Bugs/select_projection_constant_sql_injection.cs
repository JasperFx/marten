using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Security regression: a constant string projected through Select(...) is a runtime value
// (ReduceToConstant evaluates captured locals), so it can be attacker-influenced. It was
// emitted verbatim into the jsonb_build_object SELECT list, so a single quote could break
// out of the literal and inject SQL. The fix escapes embedded single quotes.
public class select_projection_constant_sql_injection : BugIntegrationContext
{
    public class Thing
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class Projection
    {
        public string Label { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public async Task projected_constant_string_with_quote_is_escaped_not_injected()
    {
        theSession.Store(new Thing { Name = "first" });
        await theSession.SaveChangesAsync();

        // Captured local => a runtime constant, standing in for attacker-influenced input.
        var label = "x' , (select 1) as evil, '";

        var results = await theSession.Query<Thing>()
            .Select(x => new Projection { Label = label, Name = x.Name })
            .ToListAsync();

        // If the value were injected, the query would either error or add a phantom column;
        // treated as data, the literal string round-trips intact.
        results.Count.ShouldBe(1);
        results[0].Label.ShouldBe(label);
        results[0].Name.ShouldBe("first");
    }

    [Fact]
    public async Task projected_constant_quote_is_escaped_in_generated_sql()
    {
        var label = "O'Brien";

        var cmd = theSession.Query<Thing>()
            .Select(x => new Projection { Label = label, Name = x.Name })
            .ToCommand();

        cmd.CommandText.ShouldContain("'O''Brien'");
    }
}
