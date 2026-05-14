using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.CompiledQueries;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CompiledQueryTests;

/// <summary>
/// Correctness gate for #4405 iteration 4. For each representative query
/// shape — Stateless (shape 1), Stateless multi-clause (shape 2), and Complex
/// with Include (shape 3) — runs the source-gen-path compiled query and a
/// reference uncompiled LINQ query against the same data, then asserts the
/// two produce identical results.
/// </summary>
/// <remarks>
/// <para>
/// This isn't an A/B between source-gen and runtime-codegen <i>directly</i>.
/// All compiled-query invocations in this assembly go through the source-gen
/// path — that's the entire point of the
/// <c>[assembly: JasperFx.JasperFxAssembly]</c> attribute + the
/// <c>Marten.SourceGenerator</c> analyzer reference in the .csproj. The
/// reference query (regular LINQ via <c>theSession.Query&lt;T&gt;</c>) uses
/// the canonical non-compiled path and is treated as the ground truth.
/// </para>
/// <para>
/// The <see cref="source_gen_path_is_actually_engaged_for_all_three_shapes"/>
/// fact independently verifies via <see cref="CompiledQueryHandlerRegistry"/>
/// that the generator-emitted <c>[ModuleInitializer]</c>s populated the
/// runtime registry. Without that the compiled queries below would silently
/// take the PoC's codegen bridge instead of source-gen.
/// </para>
/// </remarks>
public class CorrectnessGate: IntegrationContext
{
    private User _alpha_a, _alpha_b, _bravo_a, _bravo_b, _bravo_c;
    private Issue _alphaIssue, _bravoIssue;

    public CorrectnessGate(DefaultStoreFixture fixture): base(fixture) { }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();

        // Five users across two FirstName buckets so shape 2 paging is exercised.
        _alpha_a = new User { FirstName = "Alpha", LastName = "Asgard", UserName = "alpha_a" };
        _alpha_b = new User { FirstName = "Alpha", LastName = "Baldur", UserName = "alpha_b" };
        _bravo_a = new User { FirstName = "Bravo", LastName = "Asgard", UserName = "bravo_a" };
        _bravo_b = new User { FirstName = "Bravo", LastName = "Baldur", UserName = "bravo_b" };
        _bravo_c = new User { FirstName = "Bravo", LastName = "Cassini", UserName = "bravo_c" };
        await theStore.BulkInsertDocumentsAsync(new[] { _alpha_a, _alpha_b, _bravo_a, _bravo_b, _bravo_c });

        // Two issues for the Include test, each pointing at a different assignee.
        _alphaIssue = new Issue { Title = "Alpha bug", AssigneeId = _alpha_a.Id };
        _bravoIssue = new Issue { Title = "Bravo bug", AssigneeId = _bravo_c.Id };
        await theStore.BulkInsertDocumentsAsync(new[] { _alphaIssue, _bravoIssue });
    }

    [Fact]
    public void source_gen_path_is_actually_engaged_for_all_three_shapes()
    {
        // If these assertions fail, every other test in this class is meaningless —
        // we'd be exercising the codegen fallback bridge, not the source-gen path.
        CompiledQueryHandlerRegistry.TryGet(typeof(UserByUserNameShape), out var d1).ShouldBeTrue();
        d1!.ParameterMemberNames.ShouldBe(new[] { "UserName" });

        CompiledQueryHandlerRegistry.TryGet(typeof(UsersByFirstNamePageShape), out var d2).ShouldBeTrue();
        d2!.ParameterMemberNames.ShouldBe(new[] { "FirstNamePrefix", "Skip", "Take" });

        CompiledQueryHandlerRegistry.TryGet(typeof(IssueWithAssigneeShape), out var d3).ShouldBeTrue();
        d3!.ParameterMemberNames.ShouldBe(new[] { "Title" });
        d3.IncludeMemberNames.ShouldBe(new[] { "Assignees" });
    }

    [Fact]
    public async Task shape_1_simple_where_matches_reference_linq()
    {
        var compiled = await theSession.QueryAsync(new UserByUserNameShape { UserName = "bravo_b" });
        var reference = await theSession.Query<User>().FirstOrDefaultAsync(x => x.UserName == "bravo_b");

        compiled.ShouldNotBeNull();
        compiled.Id.ShouldBe(reference!.Id);
        compiled.UserName.ShouldBe("bravo_b");
    }

    [Fact]
    public async Task shape_1_returns_null_when_no_match()
    {
        var compiled = await theSession.QueryAsync(new UserByUserNameShape { UserName = "does_not_exist" });
        compiled.ShouldBeNull();
    }

    [Fact]
    public async Task shape_2_where_orderby_skip_take_matches_reference_linq()
    {
        var query = new UsersByFirstNamePageShape { FirstNamePrefix = "Bravo", Skip = 1, Take = 2 };
        var compiled = (await theSession.QueryAsync(query)).ToList();

        var reference = await theSession.Query<User>()
            .Where(x => x.FirstName!.StartsWith("Bravo"))
            .OrderBy(x => x.LastName).ThenBy(x => x.FirstName)
            .Skip(1).Take(2)
            .ToListAsync();

        compiled.Select(x => x.UserName).ShouldBe(reference.Select(x => x.UserName));
        compiled.Count.ShouldBe(2);
        compiled[0].UserName.ShouldBe("bravo_b"); // first Bravo by LastName after skipping bravo_a
        compiled[1].UserName.ShouldBe("bravo_c");
    }

    [Fact]
    public async Task shape_2_paging_changes_correctly_with_skip()
    {
        // Re-running the same compiled query type with different Skip values
        // exercises the BindParameter switch dispatching the same memberName
        // ("Skip") to different runtime values across calls.
        var page1 = (await theSession.QueryAsync(new UsersByFirstNamePageShape
            { FirstNamePrefix = "Bravo", Skip = 0, Take = 1 })).ToList();
        var page2 = (await theSession.QueryAsync(new UsersByFirstNamePageShape
            { FirstNamePrefix = "Bravo", Skip = 1, Take = 1 })).ToList();

        page1.Single().UserName.ShouldBe("bravo_a");
        page2.Single().UserName.ShouldBe("bravo_b");
    }

    [Fact]
    public async Task shape_3_where_plus_include_loads_assignee()
    {
        var query = new IssueWithAssigneeShape { Title = "Alpha bug" };
        var compiled = await theSession.QueryAsync(query);

        compiled.ShouldNotBeNull();
        compiled.Id.ShouldBe(_alphaIssue.Id);
        // The Include side-channel must have been populated by the source-gen
        // path's IncludeQueryHandler wiring (SourceGeneratedComplexHandler).
        query.Assignees.Count.ShouldBe(1);
        query.Assignees.Single().Id.ShouldBe(_alpha_a.Id);
        query.Assignees.Single().UserName.ShouldBe("alpha_a");
    }

    [Fact]
    public async Task shape_3_include_collects_distinct_assignees_across_invocations()
    {
        // Each invocation of a compiled query starts with the includes collection
        // the query instance carries. The runtime path appends; we verify that
        // separate invocations of the SAME type with different parameters
        // produce distinct, correct includes.
        var alphaQuery = new IssueWithAssigneeShape { Title = "Alpha bug" };
        await theSession.QueryAsync(alphaQuery);

        var bravoQuery = new IssueWithAssigneeShape { Title = "Bravo bug" };
        await theSession.QueryAsync(bravoQuery);

        alphaQuery.Assignees.Single().Id.ShouldBe(_alpha_a.Id);
        bravoQuery.Assignees.Single().Id.ShouldBe(_bravo_c.Id);
    }
}
