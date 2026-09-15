using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events.Projections;
using Marten.PgVector.Tests.Helpers;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     #5421: the content-hash read that decides whether the model is called runs through the SESSION,
///     not a connection the projection opens for itself.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The assertion is a count of MODEL CALLS, not a count of rows</b>, and it has to be.
///         The upsert is idempotent, so the table looks exactly the same whether the hash read saw the
///         row it should have skipped or re-embedded it and wrote the identical bytes back. What a
///         hash read on the wrong connection costs is a call to the embedding provider — a network
///         round trip somebody is metered for — and nothing else in the store records that it happened.
///     </para>
///     <para>
///         The transaction is the caller's and is never committed, which is the only arrangement that
///         separates the two connections. Against a committed corpus a side connection reads the same
///         hashes the session would and the skip works fine; it is the second
///         <c>SaveChangesAsync</c> inside one caller-owned transaction that a separate connection
///         cannot see.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class projection_reads_through_the_session: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private FakeEmbeddingProvider _embedder = null!;

    public async ValueTask InitializeAsync()
    {
        _embedder = new FakeEmbeddingProvider(dimensions: 3);
        var projection = new ProductSearchProjection(_embedder);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_proj_session";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            opts.Projections.Add(projection, ProjectionLifecycle.Inline);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_proj_session"));

            opts.Events.AddEventType<ProductCreated>();
            opts.Events.AddEventType<ProductUpdated>();
            opts.Events.AddEventType<ProductDeleted>();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    [Fact]
    public async Task unchanged_content_is_not_re_embedded_across_saves_in_one_open_transaction()
    {
        var token = TestContext.Current.CancellationToken;
        var productId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(token);
        await using var tx = await conn.BeginTransactionAsync(token);

        await using var session = _store.LightweightSession(SessionOptions.ForTransaction(tx));

        session.Events.StartStream(productId, new ProductCreated(productId, "Widget", "the first copy"));
        await session.SaveChangesAsync(token);

        // A genuine change, so this one has to reach the model whatever the read is doing.
        session.Events.Append(productId, new ProductUpdated(productId, "the revised copy"));
        await session.SaveChangesAsync(token);

        var beforeTheRepeat = _embedder.RequestedTexts.Count;
        beforeTheRepeat.ShouldBe(2);

        // The same text again. Its hash is already in the table -- written by the save above, into
        // this transaction and committed nowhere -- so the only way to know that is to read on this
        // session's connection.
        session.Events.Append(productId, new ProductUpdated(productId, "the revised copy"));
        await session.SaveChangesAsync(token);

        _embedder.RequestedTexts.Count.ShouldBe(beforeTheRepeat);

        await tx.RollbackAsync(token);
    }
}
