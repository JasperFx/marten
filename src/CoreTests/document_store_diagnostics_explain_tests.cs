#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace CoreTests;

public class ExplainedParcel
{
    public Guid Id { get; set; }
    public string State { get; set; } = "";
    public int Weight { get; set; }
}

/// <summary>
/// jasperfx#1078 (marten#5768, CritterWatch#1313): Marten's side of
/// <see cref="IDocumentStoreDiagnostics.ExplainDocumentQueryAsync" />. The cross-store facts (a plan comes back,
/// nothing is written, unknown type is null, the read's refusals) are in <c>DocumentStoreDiagnosticsCompliance</c>;
/// what is here is what only PostgreSQL can say -- which index, whether the table was scanned -- and that the
/// explained statement is the read's own.
/// </summary>
public class document_store_diagnostics_explain_tests: HostedStoreContext
{
    private static readonly string ParcelType = typeof(ExplainedParcel).FullNameInCode();
    private const int RowCount = 20_000;

    [Fact]
    public async Task an_indexed_filter_reports_the_index_and_no_table_scan()
    {
        var diagnostics = await seededAsync("indexed", indexState: true);

        // 10 matching rows in 20,000: reading the state index is the plan by orders of magnitude, so the
        // assertion does not lean on a close planner cost call.
        var plan = await explainAsync(diagnostics,
            new DocumentQueryOptions(1, 10) { Where = "State == @0", Arguments = ["MO"] });

        plan.Engine.ShouldBe("PostgreSQL");
        plan.IsActual.ShouldBeTrue();
        plan.IndexesUsed.ShouldNotBeNull();
        plan.IndexesUsed.ShouldContain(x => x.Contains("state"));
        plan.ScansTable.ShouldBe(false);
        plan.RowsReturned.ShouldBe(10);
    }

    [Fact]
    public async Task an_unindexed_filter_scans_the_table_and_examines_more_than_it_returns()
    {
        var diagnostics = await seededAsync("unindexed", indexState: false);

        var plan = await explainAsync(diagnostics,
            new DocumentQueryOptions(1, 100) { Where = "Weight > @0", Arguments = [RowCount - 11] });

        // Either a Seq Scan, or (as PostgreSQL usually picks under ORDER BY id LIMIT) a walk of the whole primary
        // key filtering every row: both read the table row by row.
        plan.ScansTable.ShouldBe(true);
        plan.IndexesUsed.ShouldNotBeNull();
        plan.IndexesUsed.ShouldNotContain(x => x.Contains("weight"));
        plan.RowsReturned.ShouldBe(10);

        // Rows were read and thrown away to find the ten -- the whole table, absent an index to stop early.
        plan.RowsExamined.ShouldBe(RowCount);
    }

    [Fact]
    public async Task the_page_is_explained_not_the_whole_match()
    {
        var diagnostics = await seededAsync("paged", indexState: false);

        // Every row matches; the read's page 3 of 25 returns 25 of them.
        var plan = await explainAsync(diagnostics, new DocumentQueryOptions(3, 25));
        plan.RowsReturned.ShouldBe(25);

        var read = await diagnostics.QueryDocumentsAsync(ParcelType, new DocumentQueryOptions(3, 25));
        read.Documents.Count.ShouldBe(25);
    }

    [Fact]
    public async Task explain_refuses_a_metadata_filter_the_read_refuses()
    {
        var diagnostics = await seededAsync("refusal", indexState: false);

        var ex = await Should.ThrowAsync<DocumentCriteriaNotSupportedException>(() =>
            diagnostics.ExplainDocumentQueryAsync(ParcelType, new DocumentQueryOptions(1, 10) { CorrelationId = "c" }));
        ex.Criterion.ShouldBe(nameof(DocumentQueryOptions.CorrelationId));
    }

    [Fact]
    public async Task an_id_that_cannot_match_is_a_plan_that_read_nothing()
    {
        var diagnostics = await seededAsync("unmatchable", indexState: false);

        var plan = await diagnostics.ExplainDocumentQueryAsync(ParcelType,
            new DocumentQueryOptions(1, 10, IdEquals: "not-a-guid"));

        plan.ShouldNotBeNull();
        plan.RowsExamined.ShouldBe(0);
        plan.RowsReturned.ShouldBe(0);
        plan.ScansTable.ShouldBe(false);
        plan.PlanText.ShouldContain("No query was executed");
    }

    [Fact]
    public async Task an_id_lookup_reads_through_the_primary_key()
    {
        var diagnostics = await seededAsync("by_id", indexState: false);
        var page = await diagnostics.QueryDocumentsAsync(ParcelType, new DocumentQueryOptions(1, 1));

        var plan = await explainAsync(diagnostics,
            new DocumentQueryOptions(1, 10, IdEquals: page.Documents.Single().Id));

        plan.RowsReturned.ShouldBe(1);
        plan.IndexesUsed.ShouldNotBeNull();
        plan.IndexesUsed.ShouldContain(x => x.Contains("pkey"));
    }

    private static async Task<DocumentQueryPlan> explainAsync(IDocumentStoreDiagnostics diagnostics,
        DocumentQueryOptions options)
    {
        var plan = await diagnostics.ExplainDocumentQueryAsync(ParcelType, options);
        plan.ShouldNotBeNull();
        return plan;
    }

    private async Task<IDocumentStoreDiagnostics> seededAsync(string suffix, bool indexState)
    {
        var schema = $"{SchemaName}_{suffix}";
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
        }

        var host = await StartHostAsync(opts =>
        {
            opts.DatabaseSchemaName = schema;
            var mapping = opts.Schema.For<ExplainedParcel>();
            if (indexState)
            {
                mapping.Index(x => x.State);
            }
        });

        // Ten "MO" rows among RowCount, for the selective indexed filter.
        var store = host.Services.GetRequiredService<IDocumentStore>();
        await store.BulkInsertDocumentsAsync(Enumerable.Range(0, RowCount).Select(i => new ExplainedParcel
        {
            Id = Guid.NewGuid(), State = i % (RowCount / 10) == 0 ? "MO" : "TX", Weight = i
        }));

        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await using var analyze = conn.CreateCommand();
            analyze.CommandText = $"analyze {schema}.mt_doc_explainedparcel";
            await analyze.ExecuteNonQueryAsync();
        }

        return host.Services.GetRequiredService<IDocumentStoreDiagnostics>();
    }
}

/// <summary>
/// jasperfx#1078: the reading of PostgreSQL's JSON plan, against canned plans, so the arithmetic is pinned
/// independently of what the planner chooses on a given server.
/// </summary>
public class postgresql_plan_reader_tests
{
    [Fact]
    public void a_sequential_scan_counts_the_rows_it_threw_away()
    {
        const string json = """
            [{"Plan": {"Node Type": "Limit", "Actual Rows": 10, "Actual Loops": 1,
              "Plans": [{"Node Type": "Sort", "Actual Rows": 10, "Actual Loops": 1,
                "Plans": [{"Node Type": "Seq Scan", "Relation Name": "mt_doc_parcel", "Alias": "d",
                  "Actual Rows": 10, "Actual Loops": 1, "Rows Removed by Filter": 19990}]}]},
              "Execution Time": 3.2}]
            """;

        var plan = DocumentStore.PostgresqlPlanReader.Read(json, "mt_doc_parcel", false);

        plan.Engine.ShouldBe("PostgreSQL");
        plan.PlanText.ShouldBe(json);
        plan.IsActual.ShouldBeTrue();
        plan.RowsReturned.ShouldBe(10);
        plan.RowsExamined.ShouldBe(20_000);
        plan.ScansTable.ShouldBe(true);
        plan.IndexesUsed.ShouldBeEmpty();
    }

    [Fact]
    public void a_bitmap_scan_names_its_index_and_counts_the_heap_once()
    {
        // PostgreSQL 18 prints actual rows with two decimals, and per loop.
        const string json = """
            [{"Plan": {"Node Type": "Limit", "Actual Rows": 4.00, "Actual Loops": 1,
              "Plans": [{"Node Type": "Bitmap Heap Scan", "Relation Name": "mt_doc_parcel",
                "Actual Rows": 2.00, "Actual Loops": 2, "Rows Removed by Index Recheck": 1,
                "Plans": [{"Node Type": "Bitmap Index Scan", "Index Name": "mt_doc_parcel_idx_state",
                  "Actual Rows": 3.00, "Actual Loops": 2}]}]}}]
            """;

        var plan = DocumentStore.PostgresqlPlanReader.Read(json, "mt_doc_parcel", false);

        plan.RowsReturned.ShouldBe(4);
        plan.RowsExamined.ShouldBe(6); // (2 + 1) x 2 loops; the bitmap index scan reads no rows of the table
        plan.IndexesUsed.ShouldBe(["mt_doc_parcel_idx_state"]);
        plan.ScansTable.ShouldBe(false);
    }

    [Fact]
    public void a_primary_key_walk_that_filters_every_row_is_a_table_scan()
    {
        const string json = """
            [{"Plan": {"Node Type": "Limit", "Actual Rows": 10, "Actual Loops": 1,
              "Plans": [{"Node Type": "Index Scan", "Index Name": "pkey_mt_doc_parcel_id", "Relation Name": "mt_doc_parcel",
                "Actual Rows": 10, "Actual Loops": 1, "Filter": "(weight > 5)", "Rows Removed by Filter": 19990}]}}]
            """;

        var plan = DocumentStore.PostgresqlPlanReader.Read(json, "mt_doc_parcel", false);

        plan.ScansTable.ShouldBe(true);
        plan.RowsExamined.ShouldBe(20_000);
        plan.IndexesUsed.ShouldBe(["pkey_mt_doc_parcel_id"]);
    }

    [Fact]
    public void an_index_scan_with_a_condition_is_not_a_table_scan()
    {
        const string json = """
            [{"Plan": {"Node Type": "Index Scan", "Index Name": "pkey_mt_doc_parcel_id", "Relation Name": "mt_doc_parcel",
              "Index Cond": "(id = $1)", "Actual Rows": 1, "Actual Loops": 1}}]
            """;

        var plan = DocumentStore.PostgresqlPlanReader.Read(json, "mt_doc_parcel", false);

        plan.ScansTable.ShouldBe(false);
        plan.RowsExamined.ShouldBe(1);
    }

    [Fact]
    public void a_sequential_scan_of_a_partition_is_a_scan_of_the_document_table()
    {
        const string json = """
            [{"Plan": {"Node Type": "Append", "Actual Rows": 3, "Actual Loops": 1,
              "Plans": [{"Node Type": "Seq Scan", "Relation Name": "mt_doc_parcel_red", "Actual Rows": 3, "Actual Loops": 1}]}}]
            """;

        DocumentStore.PostgresqlPlanReader.Read(json, "mt_doc_parcel", true).ScansTable.ShouldBe(true);
        DocumentStore.PostgresqlPlanReader.Read(json, "mt_doc_parcel", false).ScansTable.ShouldBe(false);
    }
}
