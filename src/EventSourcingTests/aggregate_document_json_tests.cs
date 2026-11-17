#nullable enable
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using JasperFx.Documents;
using JasperFx.Events.Aggregation;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// jasperfx#1082 (marten#5768, CritterWatch#1314): <c>IDocumentStoreDiagnostics.AggregateDocumentJsonAsync</c>
/// rebuilds a single-stream-projected document by live-aggregating its stream through the registered
/// projection, serialized by Marten's own serializer, so a console can diff it against the stored row and say
/// whether the read model is stale or wrong. <c>DocumentTypeRef.SingleStreamProjection</c> says which types it
/// can be asked about.
/// </summary>
public class aggregate_document_json_tests: OneOffConfigurationsContext
{
    private IDocumentStoreDiagnostics configure()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Snapshot<RebuiltInvoice>(SnapshotLifecycle.Inline);
            opts.Projections.Add<RebuiltCustomerTotalsProjection>(ProjectionLifecycle.Inline);
            opts.Schema.For<RebuiltPlainDocument>();
        });

        return theStore;
    }

    private async Task<Guid> openInvoiceAsync(params decimal[] lines)
    {
        var id = Guid.NewGuid();
        await using var session = theStore.LightweightSession();
        session.Events.StartStream<RebuiltInvoice>(id,
            new object[] { new RebuiltInvoiceOpened("Acme") }
                .Concat(lines.Select(x => (object)new RebuiltInvoiceLineAdded(x))).ToArray());
        await session.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task the_rebuilt_json_matches_what_the_inline_projection_stored()
    {
        var diagnostics = configure();
        var id = await openInvoiceAsync(10m, 32.5m);

        var stored = await diagnostics.LoadDocumentAsync(typeof(RebuiltInvoice).FullNameInCode(), id.ToString(), null);
        stored.ShouldNotBeNull();

        var rebuilt = await diagnostics.AggregateDocumentJsonAsync(typeof(RebuiltInvoice).FullNameInCode(),
            id.ToString(), null, CancellationToken.None);

        rebuilt.ShouldNotBeNull();
        JsonNode.DeepEquals(JsonNode.Parse(rebuilt), JsonNode.Parse(stored.Json))
            .ShouldBeTrue($"rebuilt {rebuilt} differs from stored {stored.Json}");

        // Nothing was written: there is still one row, and it is the one the projection stored.
        await using var query = theStore.QuerySession();
        (await query.Query<RebuiltInvoice>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task a_tampered_row_diverges_from_the_rebuild()
    {
        var diagnostics = configure();
        var id = await openInvoiceAsync(10m);

        var table = theStore.Options.Storage.MappingFor(typeof(RebuiltInvoice)).TableName.QualifiedName;
        await using (var conn = new NpgsqlConnection(ConnectionSource.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"update {table} set data = data || '{{\"Tampered\": true}}'::jsonb where id = @id";
            cmd.Parameters.AddWithValue("id", id);
            (await cmd.ExecuteNonQueryAsync()).ShouldBe(1);
        }

        var stored = await diagnostics.LoadDocumentAsync(typeof(RebuiltInvoice).FullNameInCode(), id.ToString(), null);
        var rebuilt = await diagnostics.AggregateDocumentJsonAsync(typeof(RebuiltInvoice).FullNameInCode(),
            id.ToString(), null, CancellationToken.None);

        JsonNode.DeepEquals(JsonNode.Parse(rebuilt!), JsonNode.Parse(stored!.Json)).ShouldBeFalse();
    }

    [Fact]
    public async Task a_stream_that_does_not_exist_is_null()
    {
        var diagnostics = configure();

        (await diagnostics.AggregateDocumentJsonAsync(typeof(RebuiltInvoice).FullNameInCode(),
            Guid.NewGuid().ToString(), null, CancellationToken.None)).ShouldBeNull();

        // Text that is not a value of the stream identity names no stream either.
        (await diagnostics.AggregateDocumentJsonAsync(typeof(RebuiltInvoice).FullNameInCode(),
            "not-a-guid", null, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task a_plain_document_type_is_refused_naming_the_type()
    {
        var diagnostics = configure();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            diagnostics.AggregateDocumentJsonAsync(typeof(RebuiltPlainDocument).FullNameInCode(),
                Guid.NewGuid().ToString(), null, CancellationToken.None));

        ex.Message.ShouldContain(nameof(RebuiltPlainDocument));
    }

    [Fact]
    public async Task a_multi_stream_projected_type_is_refused()
    {
        var diagnostics = configure();

        // AggregatorFor<T>() would hand back the multi-stream projection's aggregator; there is no one
        // stream to rebuild one of its documents from.
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            diagnostics.AggregateDocumentJsonAsync(typeof(RebuiltCustomerTotals).FullNameInCode(), "Acme", null,
                CancellationToken.None));

        ex.Message.ShouldContain(nameof(RebuiltCustomerTotals));
    }

    [Fact]
    public async Task an_unknown_type_name_is_an_argument_exception()
    {
        var diagnostics = configure();

        await Should.ThrowAsync<ArgumentException>(() =>
            diagnostics.AggregateDocumentJsonAsync("No.Such.Type", Guid.NewGuid().ToString(), null,
                CancellationToken.None));
    }

    [Fact]
    public async Task document_types_name_the_single_stream_projection()
    {
        var diagnostics = configure();

        var types = await diagnostics.DocumentTypesAsync(CancellationToken.None);

        var expected = theStore.Options.Projections.All
            .Single(x => x is IAggregateProjection a && a.AggregateType == typeof(RebuiltInvoice)).Name;

        types.Single(x => x.TypeName == typeof(RebuiltInvoice).FullNameInCode()).SingleStreamProjection
            .ShouldBe(expected);
        types.Single(x => x.TypeName == typeof(RebuiltPlainDocument).FullNameInCode()).SingleStreamProjection
            .ShouldBeNull();
        types.Single(x => x.TypeName == typeof(RebuiltCustomerTotals).FullNameInCode()).SingleStreamProjection
            .ShouldBeNull();
    }
}

public record RebuiltInvoiceOpened(string Customer);

public record RebuiltInvoiceLineAdded(decimal Amount);

public class RebuiltInvoice
{
    public Guid Id { get; set; }
    public string Customer { get; set; } = "";
    public decimal Total { get; set; }
    public int Lines { get; set; }

    public static RebuiltInvoice Create(RebuiltInvoiceOpened e) => new() { Customer = e.Customer };

    public void Apply(RebuiltInvoiceLineAdded e)
    {
        Total += e.Amount;
        Lines++;
    }
}

public class RebuiltPlainDocument
{
    public Guid Id { get; set; }
}

public class RebuiltCustomerTotals
{
    public string Id { get; set; } = "";
    public int Invoices { get; set; }
}

public class RebuiltCustomerTotalsProjection: MultiStreamProjection<RebuiltCustomerTotals, string>
{
    public RebuiltCustomerTotalsProjection()
    {
        Identity<RebuiltInvoiceOpened>(x => x.Customer);
    }

    public void Apply(RebuiltCustomerTotals view, RebuiltInvoiceOpened e) => view.Invoices++;
}
