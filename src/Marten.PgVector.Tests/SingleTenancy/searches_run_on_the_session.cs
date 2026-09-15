using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten.Services;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     #5423: a search runs on the SESSION's connection, not one of its own.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>"The search finds the document" is the assertion that proves nothing here</b>, which is
///         why every test in this file writes through a caller-owned transaction and never commits it.
///         A search on its own pooled connection finds everything a committed corpus holds and looks
///         perfectly correct; what it cannot see is the session's own uncommitted work, and that is the
///         only shape that separates the two.
///     </para>
///     <para>
///         The reverse fact matters as much and is asserted beside it: after the transaction is rolled
///         back, a fresh session finds nothing. Without that, a test that merely found the document
///         would also pass against a search that ignored transactions entirely.
///     </para>
/// </remarks>
[Collection("Marten.PgVector")]
public class searches_run_on_the_session: IAsyncLifetime
{
    private DocumentStore _store = null!;

    public class Note
    {
        public Guid Id { get; set; }
        public string? Body { get; set; }
        public float[]? Embedding { get; set; }
    }

    public async ValueTask InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_session_conn";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            // Declared so the scan settings are actually emitted -- ResolveEfSearch returns null for a
            // member with no vector index, which would take the settings out of the batch entirely and
            // make the_scan_settings_do_not_outlive_the_search vacuous.
            opts.VectorIndex<Note>(x => x.Embedding, 3);
            opts.Schema.For<Note>().FullTextIndex();
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    private static Note NewNote(string body) => new()
    {
        Id = Guid.NewGuid(), Body = body, Embedding = [1.0f, 0.0f, 0.0f]
    };

    [Fact]
    public async Task a_vector_search_sees_the_sessions_own_uncommitted_write()
    {
        var token = TestContext.Current.CancellationToken;
        var note = NewNote("quantum sensor calibration");

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(token);
        await using var tx = await conn.BeginTransactionAsync(token);

        await using (var session = _store.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            session.Store(note);
            await session.SaveChangesAsync(token);

            var hits = await session.VectorSearchAsync<Note>(
                x => x.Embedding, new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]), 10,
                Neutral.DistanceFunction.Cosine, token);

            hits.Select(x => x.Id).ShouldContain(note.Id);

            var scored = await session.VectorSearchWithScoresAsync<Note>(
                x => x.Embedding, new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]), 10,
                Neutral.DistanceFunction.Cosine, token);

            scored.Select(x => x.Document.Id).ShouldContain(note.Id);
        }

        await tx.RollbackAsync(token);

        // The other half of the fact: the row was never committed, so nothing else can see it.
        await using var after = _store.QuerySession();
        var afterHits = await after.VectorSearchAsync<Note>(
            x => x.Embedding, new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]), 10,
            Neutral.DistanceFunction.Cosine, token);

        afterHits.Select(x => x.Id).ShouldNotContain(note.Id);
    }

    /// <summary>
    ///     Both legs of a hybrid search, since the text leg runs through the same executor.
    /// </summary>
    [Fact]
    public async Task a_hybrid_search_sees_the_sessions_own_uncommitted_write()
    {
        var token = TestContext.Current.CancellationToken;
        var note = NewNote("quantum sensor calibration");

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(token);
        await using var tx = await conn.BeginTransactionAsync(token);

        await using (var session = _store.LightweightSession(SessionOptions.ForTransaction(tx)))
        {
            session.Store(note);
            await session.SaveChangesAsync(token);

            var hits = await session.HybridSearchAsync<Note>(
                x => x.Embedding, "quantum", new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]),
                token: token);

            hits.Select(x => x.Id).ShouldContain(note.Id);
        }

        await tx.RollbackAsync(token);
    }

    /// <summary>
    ///     ⚠️ The guard on #5438's scan settings now that they ride the session's connection: they are
    ///     scoped to the search and are gone the moment it ends.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the test that would catch a <c>SET</c> written where a <c>SET LOCAL</c> belongs.
    ///         The session is pinned to a connection the test owns and holds open afterwards, which is
    ///         the only way to ask the question at all — an ordinary session hands its connection back
    ///         to Npgsql's pool, where a leaked setting is invisible until it surfaces on somebody
    ///         else's unrelated query.
    ///     </para>
    ///     <para>
    ///         A limit of 200 rather than something small on purpose: <c>ResolveEfSearch</c> floors at
    ///         pgvector's own default of 40, so a small search sets the GUC to the value it already had
    ///         and a leak would be indistinguishable from a clean scope.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task the_scan_settings_do_not_outlive_the_search()
    {
        var token = TestContext.Current.CancellationToken;

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync(token);

        // Not disposed before the assertion: a session disposes the connection it was handed, and a
        // closed connection cannot be asked what it is still carrying.
        var session = _store.LightweightSession(SessionOptions.ForConnection(conn));

        await session.VectorSearchAsync<Note>(
            x => x.Embedding, new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]), 200,
            Neutral.DistanceFunction.Cosine, token);

        await using var cmd = new NpgsqlCommand(
            "select current_setting('hnsw.ef_search'), current_setting('hnsw.iterative_scan')", conn);
        await using var reader = await cmd.ExecuteReaderAsync(token);

        (await reader.ReadAsync(token)).ShouldBeTrue();

        // pgvector's own defaults, i.e. this connection carries nothing the search put on it.
        reader.GetString(0).ShouldBe("40");
        reader.GetString(1).ShouldBe("off");
    }

    /// <summary>
    ///     A token that is already cancelled stops the search rather than being ignored.
    /// </summary>
    /// <remarks>
    ///     The token-taking entry points are OVERLOADS rather than a defaulted parameter on the shipped
    ///     signatures, so this also pins that the new overloads are the ones a six-argument call binds
    ///     to — a call that silently bound back to the token-less shape would pass every other
    ///     assertion in this file.
    /// </remarks>
    [Fact]
    public async Task a_cancelled_token_stops_a_vector_search()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await using var session = _store.QuerySession();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await session.VectorSearchAsync<Note>(
                x => x.Embedding, new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]), 10,
                Neutral.DistanceFunction.Cosine, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await session.HybridSearchAsync<Note>(
                x => x.Embedding, "quantum", new ReadOnlyMemory<float>([1.0f, 0.0f, 0.0f]),
                token: source.Token));
    }
}
