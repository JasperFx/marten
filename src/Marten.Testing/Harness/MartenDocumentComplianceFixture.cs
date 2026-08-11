using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.ComplianceTests;
using JasperFx.Events.Documents;

namespace Marten.Testing.Harness;

/// <summary>
/// Marten's implementation of the cross-store <em>document</em> compliance seam (#5216,
/// jasperfx#647).
/// </summary>
/// <remarks>
/// <para>
/// Three members wide, against the nine-plus of <see cref="MartenComplianceFixture" />, and unlike
/// that one it is not generic over Marten's session pair. Both differences are the point rather
/// than an inconsistency: the event surface is largely only reachable through a product's own
/// session type, whereas everything the document suites do runs through
/// <see cref="IDocumentSessionFactory" /> and the three session contracts — which Marten's
/// <c>IDocumentStore</c>, <c>IQuerySession</c>, <c>IDocumentOperations</c> and <c>IDocumentSession</c>
/// now implement directly.
/// </para>
/// <para>
/// <see cref="Sessions" /> being typed as the bare <see cref="IDocumentSessionFactory" /> is what
/// makes that claim load-bearing: if any of these suites ever needed to reach past the contract
/// onto a Marten type, it would not compile.
/// </para>
/// </remarks>
public class MartenDocumentComplianceFixture: DocumentStorageComplianceFixture
{
    private DocumentStore _store = null!;

    public override IDocumentSessionFactory Sessions => _store;

    protected override async Task BuildStoreAsync(DocumentComplianceConfig config)
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);
        options.AutoCreateSchemaObjects = AutoCreate.All;
        options.DisableNpgsqlLogging = true;
        options.NameDataLength = 100;
        options.DatabaseSchemaName = (config.SchemaName ?? "doc_compliance").ToLowerInvariant();

        // Marten creates document storage on demand, so DocumentTypes is not strictly required
        // here. Registering it anyway means ApplyAllConfiguredChangesToDatabaseAsync below actually
        // builds the tables up front, which keeps the first test of each suite off the lazy-DDL
        // path and makes CleanDocumentDataAsync a plain delete rather than a no-op against nothing.
        foreach (var documentType in config.DocumentTypes)
        {
            options.Storage.MappingFor(documentType);
        }

        _store = new DocumentStore(options);

        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Wiping a store is administration rather than part of the document contract, so this is
    /// spelled on Marten's own Advanced surface — exactly the asymmetry the seam exists to absorb.
    /// </summary>
    public override Task CleanDocumentDataAsync() => _store.Advanced.Clean.DeleteAllDocumentsAsync();

    public override async ValueTask DisposeAsync()
    {
        if (_store != null)
        {
            await _store.DisposeAsync().ConfigureAwait(false);
        }
    }
}
