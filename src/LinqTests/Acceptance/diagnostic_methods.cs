using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class diagnostic_methods: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    [Fact]
    public async Task retrieves_query_plan()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 6,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();

        var plan = await theSession.Query<SimpleUser>().ExplainAsync();
        plan.ShouldNotBeNull();
        plan.PlanWidth.ShouldBeGreaterThan(0);
        plan.PlanRows.ShouldBeGreaterThan(0);
        plan.TotalCost.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task retrieves_query_plan_with_where()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 6,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();

        var plan = await theSession.Query<SimpleUser>().Where(u => u.Number > 5).ExplainAsync();
        plan.ShouldNotBeNull();
        plan.PlanWidth.ShouldBeGreaterThan(0);
        plan.PlanRows.ShouldBeGreaterThan(0);
        plan.TotalCost.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task retrieves_query_plan_with_where_and_all_options_enabled()
    {
        var user1 = new SimpleUser
        {
            UserName = "Mr Fouine",
            Number = 5,
            Birthdate = new DateTime(1986, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        var user2 = new SimpleUser
        {
            UserName = "Mrs Fouine",
            Number = 6,
            Birthdate = new DateTime(1987, 10, 4),
            Address = new SimpleAddress { HouseNumber = "12bis", Street = "rue de la martre" }
        };
        theSession.Store(user1, user2);
        await theSession.SaveChangesAsync();

        var plan = await theSession.Query<SimpleUser>().Where(u => u.Number > 5)
            .OrderBy(x => x.Number)
            .ExplainAsync(c =>
            {
                c
                    .Analyze()
                    .Buffers()
                    .Costs()
                    .Timing()
                    .Verbose();
            });


        plan.ShouldNotBeNull();
        plan.ActualTotalTime.ShouldBeGreaterThan(0m);
        plan.PlanningTime.ShouldBeGreaterThan(0m);
        plan.ExecutionTime.ShouldBeGreaterThan(0m);
        plan.SortKey.ShouldContain("(((d.data ->> 'Number'::text))::integer)");
        plan.Plans.ShouldNotBeEmpty();
    }


    [Fact]
    public void preview_basic_select_command()
    {
        var cmd = theSession.Query<Target>().ToCommand(FetchType.FetchMany);

        _output.WriteLine(cmd.CommandText);

        cmd.CommandText.ShouldBe($"select d.id, d.data from {SchemaName}.mt_doc_target as d;");
        cmd.Parameters.Any().ShouldBeFalse();
    }

    [Fact]
    public void preview_select_many()
    {
        var cmd = theSession.Query<Target>().SelectMany(x => x.Children).Where(x => x.Flag)
            .ToCommand(FetchType.FetchMany);

        _output.WriteLine(cmd.CommandText);
    }

    [Fact]
    public void preview_command_with_where_and_parameters()
    {
        var cmd = theSession.Query<Target>().Where(x => x.Number == 3 && x.Double > 2).ToCommand(FetchType.FetchMany);

        cmd.CommandText.ShouldBe($"select d.id, d.data from {SchemaName}.mt_doc_target as d where (CAST(d.data ->> 'Number' as integer) = :p0 and CAST(d.data ->> 'Double' as double precision) > :p1);");

        cmd.Parameters.Count.ShouldBe(2);
        cmd.Parameters["p0"].Value.ShouldBe(3);
        cmd.Parameters["p1"].Value.ShouldBe(2);
    }

    [Fact]
    public void preview_basic_count_command()
    {
        var cmd = theSession.Query<Target>().ToCommand(FetchType.Count);

        cmd.CommandText.ShouldBe($"select count(*) as number from {SchemaName}.mt_doc_target as d;");
    }

    [Fact]
    public void preview_basic_any_command()
    {
        var cmd = theSession.Query<Target>().ToCommand(FetchType.Any);

        cmd.CommandText.ShouldBe($"select TRUE as result from {SchemaName}.mt_doc_target as d LIMIT :p0;");
    }

    [Fact]
    public void preview_select_on_query()
    {
        var cmd = theSession.Query<Target>().OrderBy(x => x.Double).ToCommand(FetchType.FetchOne);

        cmd.CommandText.Trim().ShouldBe($"select d.id, d.data from {SchemaName}.mt_doc_target as d order by CAST(d.data ->> 'Double' as double precision) LIMIT :p0;");
    }


    public diagnostic_methods(ITestOutputHelper output)
    {
        _output = output;
    }
}
