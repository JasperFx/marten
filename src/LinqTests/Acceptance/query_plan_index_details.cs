using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace LinqTests.Acceptance;

/// <summary>
/// #6018: QueryPlan maps the keys of PostgreSQL's JSON EXPLAIN that say which index a node used and what it
/// filtered on.
/// </summary>
public class query_plan_index_details: OneOffConfigurationsContext
{
    private async Task storeDocuments(Action<StoreOptions> configure)
    {
        StoreOptions(configure);

        var docs = Enumerable.Range(0, 10000).Select(i => new PlannedDoc
        {
            Id = Guid.NewGuid(),
            Number = i,
            Tags = [$"tag{i}"]
        }).ToArray();

        await theStore.BulkInsertDocumentsAsync(docs);

        // So the planner has statistics to pick an index with
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var analyze = conn.CreateCommand();
        analyze.CommandText = $"analyze {theStore.StorageFeatures.MappingFor(typeof(PlannedDoc)).TableName.QualifiedName}";
        await analyze.ExecuteNonQueryAsync();
    }

    private static IEnumerable<QueryPlan> allNodes(QueryPlan plan)
    {
        yield return plan;
        foreach (var child in plan.Plans ?? [])
        {
            foreach (var node in allNodes(child))
            {
                yield return node;
            }
        }
    }

    [Fact]
    public async Task an_index_scan_names_its_index_condition_and_direction()
    {
        await storeDocuments(opts =>
            opts.Schema.For<PlannedDoc>().Index(x => x.Number, x => x.Name = "idx_planned_doc_number"));

        var plan = await theSession.Query<PlannedDoc>().Where(x => x.Number == 4321).ExplainAsync();

        var scan = allNodes(plan).Single(x => x.IndexName != null);
        scan.NodeType.ShouldBe("Index Scan");
        scan.IndexName.ShouldBe("idx_planned_doc_number");
        scan.IndexCondition.ShouldContain("Number");
        scan.ScanDirection.ShouldBe("Forward");

        var backward = await theSession.Query<PlannedDoc>().OrderByDescending(x => x.Number).Take(1).ExplainAsync();
        var backwardScan = allNodes(backward).Single(x => x.IndexName != null);
        backwardScan.IndexName.ShouldBe("idx_planned_doc_number");
        backwardScan.ScanDirection.ShouldBe("Backward");
    }

    [Fact]
    public async Task a_bitmap_scan_names_the_index_and_the_recheck_condition()
    {
        // A GIN index is only ever read through a bitmap scan
        await storeDocuments(opts => opts.Schema.For<PlannedDoc>().GinIndexJsonData());

        var plan = await theSession.Query<PlannedDoc>().Where(x => x.Tags.Contains("tag7")).ExplainAsync();

        var heap = allNodes(plan).Single(x => x.NodeType == "Bitmap Heap Scan");
        heap.RecheckCondition.ShouldContain("data @>");
        heap.IndexName.ShouldBeNull();

        var index = allNodes(plan).Single(x => x.NodeType == "Bitmap Index Scan");
        index.IndexName.ShouldBe("mt_doc_planneddoc_idx_data");
        index.IndexCondition.ShouldContain("data @>");
    }

    [Fact]
    public async Task a_sequential_scan_reports_its_filter_and_with_analyze_the_rows_it_removed()
    {
        await storeDocuments(_ => { });

        var plan = await theSession.Query<PlannedDoc>().Where(x => x.Number < 100)
            .ExplainAsync(configureExplain: x => x.Analyze());

        var scan = allNodes(plan).Single(x => x.NodeType == "Seq Scan");
        scan.IndexName.ShouldBeNull();
        scan.IndexCondition.ShouldBeNull();
        scan.Filter.ShouldContain("Number");
        scan.RowsRemovedByFilter.ShouldBe(9900);

        // Without ANALYZE there is a filter but no count of what it removed
        var estimated = await theSession.Query<PlannedDoc>().Where(x => x.Number < 100).ExplainAsync();
        var estimatedScan = allNodes(estimated).Single(x => x.NodeType == "Seq Scan");
        estimatedScan.Filter.ShouldContain("Number");
        estimatedScan.RowsRemovedByFilter.ShouldBeNull();
    }
}

public class PlannedDoc
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public List<string> Tags { get; set; } = new();
}
