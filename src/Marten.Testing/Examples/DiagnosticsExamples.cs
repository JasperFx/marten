using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace Marten.Testing.Examples;

#region sample_trade_document_type
public class Trade
{
    public int Id { get; set; }

    [DuplicateField]
    public double Value { get; set; }
}

#endregion

public class DiagnosticsExamples: IntegrationContext
{
    public async Task use_diagnostics()
    {
        // Marten is NOT coupled to StructureMap, but we
        // use it in our test suite for convenience

        #region sample_preview_linq_command
        // store is the active IDocumentStore
        var queryable = theStore.QuerySession().Query<Trade>().Where(x => x.Value > 2000);
        var cmd = queryable.ToCommand(FetchType.FetchMany);

        Debug.WriteLine(cmd.CommandText);
        #endregion

        #region sample_preview_linq_explain_plan
        // Explain() is an extension method off of IQueryable<T>
        var plan = await queryable.ExplainAsync();
        Console.WriteLine($"NodeType: {plan.NodeType}");
        Console.WriteLine($"RelationName: {plan.RelationName}");
        Console.WriteLine($"Alias: {plan.Alias}");
        Console.WriteLine($"StartupCost: {plan.StartupCost}");
        Console.WriteLine($"TotalCost: {plan.TotalCost}");
        Console.WriteLine($"PlanRows: {plan.PlanRows}");
        Console.WriteLine($"PlanWidth: {plan.PlanWidth}");
        #endregion
    }

    public void use_request_count()
    {
        #region sample_using_request_count
        using (var session = theStore.QuerySession())
        {
            var users = session.Query<User>().ToList();
            var count = session.Query<User>().Count();
            var any = session.Query<User>().Any();

            session.RequestCount.ShouldBe(3);
        }
        #endregion
    }

    public DiagnosticsExamples(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
