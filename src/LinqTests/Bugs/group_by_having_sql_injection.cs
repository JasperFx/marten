using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace LinqTests.Bugs;

// Security regression for GHSA-q4xm-rhx9-xjm4.
//
// A GroupBy HAVING comparison's constant/closure operand is a RUNTIME value -- TryToParseConstant
// evaluates captured locals -- so it can be attacker-influenced. GroupBySelectParser.ResolveOperand
// rendered it with ToString() and HavingComparisonFragment.Apply appended it verbatim.
//
// The sharp edge is that Marten put NO quotes around it. Unlike the other injection sinks in this
// folder there was no quote to break out of: the attacker supplied the entire literal AND whatever
// followed it. `"'' OR 1=1 --"` became `HAVING max(...) = '' OR 1=1 --`, which matches every group.
//
// The fix routes every value operand through AppendParameter, so the payload can only ever be
// compared as data. These facts assert BEHAVIOUR (the filter still filters, the payload matches
// nothing) rather than the shape of the generated SQL, because the guarantee that matters is that
// the payload cannot change the meaning of the statement.
public class group_by_having_sql_injection: BugIntegrationContext
{
    public class Item
    {
        public Guid Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Number { get; set; }
    }

    private async Task seed()
    {
        theSession.Store(
            new Item { Category = "alpha", Name = "aardvark", Number = 1 },
            new Item { Category = "alpha", Name = "antelope", Number = 2 },
            new Item { Category = "beta", Name = "badger", Number = 3 });
        await theSession.SaveChangesAsync();
    }

    /// <summary>
    ///     The reported proof of concept, verbatim. No quote-breakout needed: the whole literal is
    ///     attacker-supplied. Before the fix this returned every group.
    /// </summary>
    [Fact]
    public async Task tautology_payload_does_not_match_every_group()
    {
        await seed();

        var payload = "'' OR 1=1 --";

        var results = await theSession.Query<Item>()
            .GroupBy(x => x.Category)
            .Where(g => g.Max(x => x.Name) == payload)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        // The payload is now a single string value, and no row's max(Name) equals it.
        results.ShouldBeEmpty();
    }

    /// <summary>
    ///     The quote-breakout variant the report also gives. Same expectation.
    /// </summary>
    [Fact]
    public async Task quoted_tautology_payload_does_not_match_every_group()
    {
        await seed();

        var payload = "'x' OR 1=1 --";

        var results = await theSession.Query<Item>()
            .GroupBy(x => x.Category)
            .Where(g => g.Max(x => x.Name) == payload)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        results.ShouldBeEmpty();
    }

    /// <summary>
    ///     A payload containing a semicolon must not reach the SQL text at all.
    /// </summary>
    /// <remarks>
    ///     Measured, not assumed: on unpatched master this does NOT create the marker table. Marten
    ///     executes through <c>NpgsqlBatch</c>, and Npgsql's batch parser refuses multiple statements
    ///     outright — <c>"Specifying multiple SQL statements in a single NpgsqlBatchCommand isn't
    ///     supported, please remove all semicolons."</c> So the reported "stacked statements enable
    ///     modification" escalation is NOT reachable through this sink; the demonstrated impact is
    ///     filter/authorization bypass and subquery-based disclosure, which
    ///     <see cref="tautology_payload_does_not_match_every_group" /> covers.
    ///     <para>
    ///         The fact still earns its place: pre-fix the payload reached the command text and blew
    ///         up inside Npgsql, which is itself proof it was concatenated rather than bound.
    ///         Post-fix it is inert data and the query simply matches nothing.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task semicolon_payload_never_reaches_the_command_text()
    {
        await seed();

        const string marker = "ghsa_q4xm_injection_marker";
        var payload = $"'x'; create table public.{marker}(i int); --";

        var results = await theSession.Query<Item>()
            .GroupBy(x => x.Category)
            .Where(g => g.Max(x => x.Name) == payload)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        results.ShouldBeEmpty();

        var markerExists = await theSession.QueryAsync<bool>(
            "select exists(select 1 from information_schema.tables where table_name = ?)", marker);

        markerExists.Single().ShouldBeFalse("the stacked statement must not have executed");
    }

    /// <summary>
    ///     A single quote inside an ordinary, non-malicious value has to survive round-tripping —
    ///     escaping bugs usually show up here first.
    /// </summary>
    [Fact]
    public async Task a_legitimate_value_containing_a_quote_still_matches()
    {
        theSession.Store(
            new Item { Category = "irish", Name = "O'Brien", Number = 1 },
            new Item { Category = "other", Name = "Smith", Number = 2 });
        await theSession.SaveChangesAsync();

        var name = "O'Brien";

        var results = await theSession.Query<Item>()
            .GroupBy(x => x.Category)
            .Where(g => g.Max(x => x.Name) == name)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        results.ShouldHaveSingleItem().Category.ShouldBe("irish");
    }

    /// <summary>
    ///     The numeric path was never injectable, but it is the common case and must keep working
    ///     now that it is parameterized rather than stringified.
    /// </summary>
    [Fact]
    public async Task numeric_having_still_filters()
    {
        await seed();

        var results = await theSession.Query<Item>()
            .GroupBy(x => x.Category)
            .Where(g => g.Count() > 1)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        results.ShouldHaveSingleItem().Category.ShouldBe("alpha");
    }

    /// <summary>
    ///     A closure-captured value takes the TryToParseConstant branch rather than the
    ///     ConstantExpression branch. Both reach the same sink, so both need covering.
    /// </summary>
    [Fact]
    public async Task closure_captured_payload_is_also_parameterized()
    {
        await seed();

        var payload = "'' OR 1=1 --";
        Func<string> capture = () => payload;
        var fromClosure = capture();

        var results = await theSession.Query<Item>()
            .GroupBy(x => x.Category)
            .Where(g => g.Max(x => x.Name) == fromClosure)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .ToListAsync();

        results.ShouldBeEmpty();
    }
}
