using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Documents;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Metadata;

/// <summary>
/// Coverage for marten#4791 / JasperFx/CritterWatch#629 — exact-match metadata filters
/// on <see cref="IDocumentStoreDiagnostics.QueryDocumentsAsync"/>. Each of the three new
/// filters (<see cref="DocumentQueryOptions.CorrelationId"/>,
/// <see cref="DocumentQueryOptions.CausationId"/>,
/// <see cref="DocumentQueryOptions.LastModifiedBy"/>) is honored only when both the option
/// is set AND the document mapping has the corresponding metadata column enabled — emitting
/// a WHERE on a column that doesn't exist would throw <c>42703 undefined_column</c>.
///
/// Seeding strategy: 8 documents whose metadata cover every combination of
/// <c>correlation ∈ {c0, c1}</c> × <c>causation ∈ {u0, u1}</c> × <c>last_modified_by ∈ {b0, b1}</c>,
/// indexed 0..7 by the (corr, caus, lmb) binary tuple. Each filter Theory case names a target
/// value per axis (or null) and asserts the exact expected subset of docs returns.
/// </summary>
public class document_diagnostics_metadata_filter_tests
{
    /// <summary>
    /// Every permutation of the three doc metadata filters being independently on/off
    /// (2^3 = 8 combos). All three metadata columns are enabled on the mapping in this
    /// suite; the "filter set but column disabled" cases are pinned in the dedicated
    /// <see cref="filter_is_silently_ignored_when_correlation_id_column_is_disabled"/>
    /// / <see cref="filter_is_silently_ignored_when_causation_id_column_is_disabled"/>
    /// / <see cref="filter_is_silently_ignored_when_last_modified_by_column_is_disabled"/>
    /// facts below.
    /// </summary>
    [Theory]
    // (corrFilter, causFilter, lmbFilter,   expectedDocIndices)
    [InlineData(null,  null, null, new[] { 0, 1, 2, 3, 4, 5, 6, 7 })] // no filter: every doc
    [InlineData("c0",  null, null, new[] { 0, 1, 2, 3 })]               // corr=c0 only: 4 docs
    [InlineData(null,  "u0", null, new[] { 0, 1, 4, 5 })]               // caus=u0 only: 4 docs
    [InlineData(null,  null, "b0", new[] { 0, 2, 4, 6 })]               // lmb=b0 only:  4 docs
    [InlineData("c0",  "u0", null, new[] { 0, 1 })]                     // corr+caus
    [InlineData("c0",  null, "b0", new[] { 0, 2 })]                     // corr+lmb
    [InlineData(null,  "u0", "b0", new[] { 0, 4 })]                     // caus+lmb
    [InlineData("c0",  "u0", "b0", new[] { 0 })]                        // all three: one doc
    public async Task every_filter_combo_returns_the_expected_subset(
        string? corr, string? caus, string? lmb, int[] expectedIndices)
    {
        const string schema = "diag4791_combo";
        using var host = await BuildHostWithAllMetadataEnabled(schema);
        await seedMatrixOfEightDocs(host);

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50)
            {
                CorrelationId = corr,
                CausationId = caus,
                LastModifiedBy = lmb
            },
            CancellationToken.None);

        result.TotalCount.ShouldBe(expectedIndices.Length);
        var returnedNames = result.DocumentsJson
            .Select(json => JsonDocument.Parse(json).RootElement.GetProperty("Name").GetString())
            .ToList();
        returnedNames.ShouldBe(expectedIndices.Select(NameForIndex).ToList(), ignoreOrder: true);
    }

    [Fact]
    public async Task filter_on_an_unmatched_value_returns_zero_rows()
    {
        const string schema = "diag4791_unmatched";
        using var host = await BuildHostWithAllMetadataEnabled(schema);
        await seedMatrixOfEightDocs(host);

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50)
            {
                CorrelationId = "no-such-correlation"
            },
            CancellationToken.None);

        result.TotalCount.ShouldBe(0);
        result.DocumentsJson.Count.ShouldBe(0);
    }

    [Fact]
    public async Task filter_is_silently_ignored_when_correlation_id_column_is_disabled()
    {
        // CorrelationId column NOT enabled on the mapping. Setting the filter must NOT
        // throw 42703 undefined_column — the implementation skips the WHERE entirely.
        const string schema = "diag4791_corr_off";
        using var host = await BuildHost(schema, enableCorr: false, enableCaus: true, enableLmb: true);
        await seedMatrixOfEightDocs(host);

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50)
            {
                CorrelationId = "c0" // would otherwise narrow to 4 docs
            },
            CancellationToken.None);

        // Filter silently ignored → every seeded doc returns.
        result.TotalCount.ShouldBe(8);
    }

    [Fact]
    public async Task filter_is_silently_ignored_when_causation_id_column_is_disabled()
    {
        const string schema = "diag4791_caus_off";
        using var host = await BuildHost(schema, enableCorr: true, enableCaus: false, enableLmb: true);
        await seedMatrixOfEightDocs(host);

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50)
            {
                CausationId = "u0"
            },
            CancellationToken.None);

        result.TotalCount.ShouldBe(8);
    }

    [Fact]
    public async Task filter_is_silently_ignored_when_last_modified_by_column_is_disabled()
    {
        const string schema = "diag4791_lmb_off";
        using var host = await BuildHost(schema, enableCorr: true, enableCaus: true, enableLmb: false);
        await seedMatrixOfEightDocs(host);

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50)
            {
                LastModifiedBy = "b0"
            },
            CancellationToken.None);

        result.TotalCount.ShouldBe(8);
    }

    [Fact]
    public async Task filter_set_alongside_an_enabled_filter_when_a_column_is_disabled_still_narrows_via_the_enabled_one()
    {
        // Mixed scenario: causation disabled, correlation enabled. Setting BOTH filters
        // must still narrow the result set via the enabled column — the disabled-column
        // filter is silently ignored, NOT propagated as "no rows".
        const string schema = "diag4791_mixed";
        using var host = await BuildHost(schema, enableCorr: true, enableCaus: false, enableLmb: true);
        await seedMatrixOfEightDocs(host);

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        var result = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50)
            {
                CorrelationId = "c0",   // honored — 4 docs
                CausationId = "u0",      // silently ignored — column disabled
                LastModifiedBy = "b0"    // honored — narrows to 2
            },
            CancellationToken.None);

        // Without causation filtering: corr=c0 (4) ∩ lmb=b0 (4 from disjoint axis) = docs 0, 2.
        result.TotalCount.ShouldBe(2);
        var returnedNames = result.DocumentsJson
            .Select(json => JsonDocument.Parse(json).RootElement.GetProperty("Name").GetString())
            .ToList();
        returnedNames.ShouldBe(new[] { NameForIndex(0), NameForIndex(2) }, ignoreOrder: true);
    }

    [Fact]
    public async Task IdEquals_and_metadata_filters_compose_with_AND()
    {
        // Existing IdEquals filter conjoins with the new metadata filters — verify that the
        // implementation truly ANDs them rather than overwriting either side.
        const string schema = "diag4791_id_and_meta";
        using var host = await BuildHostWithAllMetadataEnabled(schema);
        await seedMatrixOfEightDocs(host);

        var store = host.Services.GetRequiredService<IDocumentStore>();
        await using var session = store.QuerySession();
        var doc0 = await session.Query<DiagMetaDoc>().FirstAsync(x => x.Name == NameForIndex(0));

        var diagnostics = host.Services.GetRequiredService<IDocumentStoreDiagnostics>();

        // Target doc 0 by id AND by all three matching metadata values — should return that single row.
        var hit = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50, IdEquals: doc0.Id.ToString())
            {
                CorrelationId = "c0",
                CausationId = "u0",
                LastModifiedBy = "b0"
            },
            CancellationToken.None);
        hit.TotalCount.ShouldBe(1);

        // Same id but with a metadata value that doesn't match doc 0 → empty result.
        var miss = await diagnostics.QueryDocumentsAsync(
            typeof(DiagMetaDoc).FullName!,
            new DocumentQueryOptions(PageNumber: 1, PageSize: 50, IdEquals: doc0.Id.ToString())
            {
                CorrelationId = "c1" // doc 0 has c0
            },
            CancellationToken.None);
        miss.TotalCount.ShouldBe(0);
    }

    private static Task<IHost> BuildHostWithAllMetadataEnabled(string schema) =>
        BuildHost(schema, enableCorr: true, enableCaus: true, enableLmb: true);

    private static async Task<IHost> BuildHost(string schema, bool enableCorr, bool enableCaus, bool enableLmb)
    {
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await conn.DropSchemaAsync(schema);
            await conn.CloseAsync();
        }

        return await Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddMarten(opts =>
                {
                    opts.Connection(ConnectionSource.ConnectionString);
                    opts.DatabaseSchemaName = schema;

                    opts.Schema.For<DiagMetaDoc>().Metadata(m =>
                    {
                        if (enableCorr) m.CorrelationId.Enabled = true;
                        if (enableCaus) m.CausationId.Enabled = true;
                        if (enableLmb) m.LastModifiedBy.Enabled = true;
                    });
                });
            })
            .StartAsync();
    }

    /// <summary>
    /// Seed 8 docs with metadata covering every (correlation × causation × last_modified_by) combo.
    /// Each doc's Name encodes its index so tests can assert exact row identity. A separate session
    /// per doc lets each carry its own session-scoped metadata (CorrelationId / CausationId /
    /// LastModifiedBy are session properties applied at SaveChanges time).
    /// </summary>
    private static async Task seedMatrixOfEightDocs(IHost host)
    {
        var store = host.Services.GetRequiredService<IDocumentStore>();

        for (var i = 0; i < 8; i++)
        {
            await using var session = store.LightweightSession();
            session.CorrelationId = (i & 0b100) == 0 ? "c0" : "c1";
            session.CausationId = (i & 0b010) == 0 ? "u0" : "u1";
            session.LastModifiedBy = (i & 0b001) == 0 ? "b0" : "b1";

            session.Store(new DiagMetaDoc { Id = Guid.NewGuid(), Name = NameForIndex(i) });
            await session.SaveChangesAsync();
        }
    }

    private static string NameForIndex(int i) => $"doc-{i}";
}

public class DiagMetaDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}
