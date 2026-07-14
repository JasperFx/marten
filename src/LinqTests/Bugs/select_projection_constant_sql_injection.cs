using System;
using System.Linq;
using System.Text.Json;
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

    public class ObjectProjection
    {
        public object Label { get; set; } = new();
        public string Name { get; set; } = string.Empty;
    }

    public class NumberProjection
    {
        public int Number { get; set; }
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

    // Security regression (incomplete-fix/type variant of the string case above): a non-string
    // projection constant was emitted as raw.ToString() with no escaping or parameterization.
    // An object-typed request property bound by System.Text.Json arrives as a JsonElement whose
    // ToString() is attacker-controlled text, so it could break out of the jsonb_build_object
    // SELECT list. The fix binds every non-string constant as a command parameter.
    [Fact]
    public async Task projected_jsonelement_constant_is_parameterized_not_injected()
    {
        theSession.Store(new Thing { Name = "first" });
        await theSession.SaveChangesAsync();

        // Mirrors an `object`-typed request property that System.Text.Json materializes as a JsonElement.
        using var doc = JsonDocument.Parse("\"(select 1)) as evil, (select 1\"");
        object label = doc.RootElement.Clone();

        var results = await theSession.Query<Thing>()
            .Select(x => new ObjectProjection { Label = label, Name = x.Name })
            .ToListAsync();

        // Treated as data, the value round-trips intact rather than injecting a phantom column.
        results.Count.ShouldBe(1);
        results[0].Label.ToString().ShouldBe("(select 1)) as evil, (select 1");
        results[0].Name.ShouldBe("first");
    }

    [Fact]
    public void projected_jsonelement_constant_is_bound_as_parameter()
    {
        using var doc = JsonDocument.Parse("\"(select secret from secrets limit 1)) --\"");
        object label = doc.RootElement.Clone();

        var cmd = theSession.Query<Thing>()
            .Select(x => new ObjectProjection { Label = label, Name = x.Name })
            .ToCommand();

        // The attacker text must be absent from the SQL grammar and present in a parameter.
        cmd.CommandText.ShouldNotContain("select secret");
        cmd.Parameters.Count.ShouldBeGreaterThan(0);
        cmd.Parameters.ShouldContain(p => Equals(p.Value, "(select secret from secrets limit 1)) --"));
    }

    [Fact]
    public async Task projected_numeric_constant_is_bound_as_parameter()
    {
        theSession.Store(new Thing { Name = "first" });
        await theSession.SaveChangesAsync();

        var number = 42;

        var cmd = theSession.Query<Thing>()
            .Select(x => new NumberProjection { Number = number, Name = x.Name })
            .ToCommand();

        cmd.Parameters.ShouldContain(p => Equals(p.Value, 42));

        // A numeric constant still round-trips as a JSON number, not a quoted string.
        var results = await theSession.Query<Thing>()
            .Select(x => new NumberProjection { Number = number, Name = x.Name })
            .ToListAsync();

        results.Single().Number.ShouldBe(42);
    }
}
