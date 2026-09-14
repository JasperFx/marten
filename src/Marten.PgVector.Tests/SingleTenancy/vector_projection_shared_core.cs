using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using JasperFx.Events.Projections;
using JasperFx.Events.Vectors;
using Marten.PgVector.Projection;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Neutral = JasperFx.Events.Vectors;

namespace Marten.PgVector.Tests.SingleTenancy;

/// <summary>
///     ⚠️ <b>The measurement that had to happen before adopting
///     <see cref="Neutral.VectorEmbeddingPlan{TId}" />, because getting it wrong costs a customer
///     money rather than correctness.</b> The stored content hash is what decides whether the
///     embedding model is called at all. If the shared spelling and Marten's differ, every hash on
///     disk reads as "changed" and an entire corpus is re-embedded once, at the provider's meter, for
///     no change in content.
/// </summary>
public class content_hash_spelling
{
    /// <summary>The pre-9.37 Marten spelling, reproduced exactly.</summary>
    private static string MartensOldHash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>
    ///     ⚠️ <b>They DIFFER, and the difference is the case of the hex.</b> Marten used
    ///     <c>Convert.ToHexString</c> (uppercase); the shared plan uses <c>Convert.ToHexStringLower</c>.
    ///     Same SHA-256, same bytes, different string — so a byte-for-byte comparison of the two finds
    ///     every document changed.
    /// </summary>
    [Fact]
    public void marten_hashed_the_same_bytes_in_a_different_case()
    {
        const string content = "A fantastic widget for all purposes";

        var shared = Neutral.VectorEmbeddingPlan<Guid>.HashOf(content);
        var marten = MartensOldHash(content);

        shared.ShouldNotBe(marten);
        shared.ShouldBe(marten.ToLowerInvariant());
        shared.ShouldBe(shared.ToLowerInvariant());
    }
}

public record MemoWritten(Guid MemoId, string Body);

public record MemoRetracted(Guid MemoId);

public record NoteWritten(string NoteKey, string Body);

public record NoteTitled(string NoteKey, string Title);

/// <summary>A provider that counts, so a test can assert the model was NOT called.</summary>
public class CountingEmbeddingProvider: Neutral.IEmbeddingProvider
{
    public int Dimensions => 3;

    public int Calls { get; private set; }

    public int TextsEmbedded { get; private set; }

    public Task<ReadOnlyMemory<float>[]> GenerateEmbeddingsAsync(string[] texts, CancellationToken ct = default)
    {
        Calls++;
        TextsEmbedded += texts.Length;

        return Task.FromResult(texts
            .Select(text =>
            {
                var hash = Math.Abs(text.GetHashCode());
                return (ReadOnlyMemory<float>)new[]
                {
                    (float)Math.Sin(hash), (float)Math.Cos(hash), (float)Math.Sin(hash * 2)
                };
            })
            .ToArray());
    }
}

public class MemoVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection("memo_vectors", provider)
{
    protected override void Configure(VectorProjectionMapping map)
    {
        map.Map<MemoWritten>(e => e.Body, e => e.MemoId);
        map.Delete<MemoRetracted>(e => e.MemoId);
    }
}

#region sample_pgvector_generic_vector_projection

/// <summary>
///     Keyed on the stream KEY rather than a Guid, which the pre-9.37 projection could not express at
///     all — its id was hardcoded to <see cref="Guid" /> (marten#5424).
/// </summary>
public class NoteVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection<string>("note_vectors", provider)
{
    protected override void Configure(Neutral.VectorProjectionMap<string> map)
    {
        // The shared map's selector sees the IEvent<T> wrapper, so stream identity and metadata are
        // in reach as well as the body.
        map.Map<NoteWritten>(e => e.Data.Body, e => e.Data.NoteKey);
    }
}

#endregion

#region sample_pgvector_map_from_aggregate

public class Note
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";

    public void Apply(NoteWritten e) => Body = e.Body;
    public void Apply(NoteTitled e) => Title = e.Title;
}

/// <summary>
///     ⚠️ A content selector that sees only one event cannot keep an embedding correct across a
///     partial-update event: given <c>NoteTitled</c>, returning the new title re-embeds the note
///     without its body, and returning null leaves the embedding stale. Building the text from the
///     aggregate's CURRENT STATE is the third answer.
/// </summary>
public class NoteAggregateVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection<string>("note_aggregate_vectors", provider)
{
    protected override void Configure(Neutral.VectorProjectionMap<string> map)
    {
        map.MapFromAggregate<Note>(
            note => $"{note.Title} {note.Body}".Trim(),
            (typeof(NoteWritten), e => e.StreamKey!),
            (typeof(NoteTitled), e => e.StreamKey!));
    }
}

#endregion

/// <summary>
///     <see cref="VectorProjection{TId}" /> over <see cref="Neutral.VectorProjectionMap{TId}" /> and
///     <see cref="Neutral.VectorEmbeddingPlan{TId}" /> (jasperfx#841), and the four defects the shared
///     versions do not have.
/// </summary>
[Collection("Marten.PgVector")]
public class vector_projection_shared_core: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private CountingEmbeddingProvider _provider = null!;
    private MemoVectorProjection _memos = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = new CountingEmbeddingProvider();
        _memos = new MemoVectorProjection(_provider);
        var notes = new NoteVectorProjection(_provider);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_shared_core";
            opts.Events.DatabaseSchemaName = "pgvector_shared_core";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            opts.Events.StreamIdentity = StreamIdentity.AsString;

            opts.Projections.Add(notes, ProjectionLifecycle.Inline);

            opts.Storage.ExtendedSchemaObjects.Add(_memos.BuildTable("pgvector_shared_core"));
            opts.Storage.ExtendedSchemaObjects.Add(notes.BuildTable("pgvector_shared_core"));
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>
    ///     ⚠️ <b>The adoption is free rather than a re-embedding bill.</b> A row written by the
    ///     pre-9.37 projection carries an UPPERCASE hash. The projection normalises what it reads, so
    ///     unchanged content still matches and the model is not called — which is the difference
    ///     between adopting the shared plan and re-embedding a customer's whole corpus once.
    /// </summary>
    [Fact]
    public async Task an_uppercase_hash_written_by_the_old_projection_still_counts_as_unchanged()
    {
        var memoId = Guid.NewGuid();
        const string body = "A fantastic widget for all purposes";

        await using (var seeding = _store.LightweightSession())
        {
            // Exactly what the pre-9.37 projection wrote: uppercase hex.
            seeding.QueueSqlCommand(
                "insert into pgvector_shared_core.memo_vectors "
                + "(id, embedding, content_text, content_hash, last_updated) "
                + "values (?, '[0,0,0]'::vector(3), ?, ?, now())",
                memoId,
                body,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))));

            await seeding.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var session = _store.LightweightSession();
        await _memos.ApplyAsync(
            session,
            [Event(new MemoWritten(memoId, body))],
            TestContext.Current.CancellationToken);
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        _provider.Calls.ShouldBe(0);
    }

    /// <summary>
    ///     ⚠️ <b>The write is queued on the session's unit of work, not executed on a connection of
    ///     the projection's own</b> (marten#5421). The old projection opened its own connection and
    ///     committed immediately, so an embedding survived a page the daemon rolled back — leaving the
    ///     index describing events the store does not have.
    /// </summary>
    [Fact]
    public async Task the_embedding_is_written_in_the_sessions_transaction()
    {
        var memoId = Guid.NewGuid();

        await using var session = _store.LightweightSession();
        await _memos.ApplyAsync(
            session,
            [Event(new MemoWritten(memoId, "a memo worth embedding"))],
            TestContext.Current.CancellationToken);

        // Nothing is committed yet, because the upsert is an operation in the unit of work.
        (await CountMemoRowsAsync()).ShouldBe(0);

        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await CountMemoRowsAsync()).ShouldBe(1);
    }

    /// <summary>
    ///     marten#5424: a string-identified store could not use a vector projection at all, because
    ///     <c>TId</c> was hardcoded to <see cref="Guid" /> — right down to the <c>uuid</c> column.
    /// </summary>
    [Fact]
    public async Task a_projection_can_be_keyed_on_a_string()
    {
        await using var session = _store.LightweightSession();
        session.Events.StartStream("note-1", new NoteWritten("note-1", "the body of the note"));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        var query = await session.VectorProjectionSearchAsync<string>(
            "note_vectors",
            await _provider.GenerateEmbeddingAsync("the body of the note", TestContext.Current.CancellationToken),
            limit: 5);

        query.Count.ShouldBe(1);
        query[0].Id.ShouldBe("note-1");
        query[0].ContentText.ShouldBe("the body of the note");
    }

    /// <summary>
    ///     ⚠️ A selector that throws is a bug, and the shared map lets it fault the shard rather than
    ///     catching it and returning null — which the caller reads as "no content for this event", so a
    ///     buggy selector silently drops the document out of the index with nothing reported anywhere
    ///     (marten#5420).
    /// </summary>
    [Fact]
    public async Task a_throwing_content_selector_faults_rather_than_dropping_the_document()
    {
        var projection = new ThrowingVectorProjection(_provider);

        await using var session = _store.LightweightSession();

        await Should.ThrowAsync<DivideByZeroException>(async () =>
            await projection.ApplyAsync(
                session,
                [Event(new MemoWritten(Guid.NewGuid(), "anything"))],
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A projection that declares nothing would read every event and write nothing, which the
    ///     shared map asks a store to refuse rather than run.
    /// </summary>
    [Fact]
    public void a_projection_with_no_mappings_is_refused()
    {
        var ex = Should.Throw<InvalidOperationException>(() => new EmptyVectorProjection(_provider));
        ex.Message.ShouldContain("declared no mappings");
    }

    private static IEvent Event<T>(T data) where T : notnull
        => new Event<T>(data) { Id = Guid.NewGuid(), StreamId = Guid.NewGuid(), Sequence = 1, Version = 1 };

    private async Task<int> CountMemoRowsAsync()
    {
        await using var session = _store.QuerySession();
        await using var conn = session.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from pgvector_shared_core.memo_vectors";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    private async Task<string?> ContentTextAsync(string table, string id)
    {
        await using var session = _store.QuerySession();
        await using var conn = session.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select content_text from pgvector_shared_core.{table} where id = $1";
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = id });
        return (string?)await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken);
    }
}

public class ThrowingVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection("throwing_vectors", provider)
{
    protected override void Configure(VectorProjectionMapping map)
    {
        map.Map<MemoWritten>(_ => throw new DivideByZeroException(), e => e.MemoId);
    }
}

public class EmptyVectorProjection(Neutral.IEmbeddingProvider provider)
    : VectorProjection("empty_vectors", provider)
{
    protected override void Configure(VectorProjectionMapping map)
    {
    }
}

/// <summary>
///     <see cref="Neutral.VectorProjectionMap{TId}.MapFromAggregate{T}" /> — building the embedded text
///     from the aggregate's CURRENT STATE rather than from one event.
/// </summary>
/// <remarks>
///     ⚠️ <b>Registered ASYNC, and that is not incidental.</b> The aggregate is built by live
///     aggregation over the stream, which reads COMMITTED events — so an inline registration would
///     aggregate the page that is still being written and miss the very event that triggered it. The
///     daemon runs after the commit, which is the only lifecycle where "the aggregate as it stands
///     after this page" is a question the store can answer.
/// </remarks>
[Collection("Marten.PgVector")]
public class map_from_aggregate_tests: IAsyncLifetime
{
    private DocumentStore _store = null!;
    private CountingEmbeddingProvider _provider = null!;
    private NoteAggregateVectorProjection _projection = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = new CountingEmbeddingProvider();
        var projection = new NoteAggregateVectorProjection(_provider);

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "pgvector_aggregate_map";
            opts.Events.DatabaseSchemaName = "pgvector_aggregate_map";
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.UsePgVector();

            opts.Events.StreamIdentity = StreamIdentity.AsString;

            opts.Projections.Add(projection, ProjectionLifecycle.Async);
            opts.Storage.ExtendedSchemaObjects.Add(projection.BuildTable("pgvector_aggregate_map"));
            _projection = projection;
        });

        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public ValueTask DisposeAsync()
    {
        _store?.Dispose();
        return default;
    }

    /// <summary>
    ///     ⚠️ <c>NoteTitled</c> carries only the title, so a per-event selector would have to choose
    ///     between re-embedding the note without its body and leaving the embedding stale. Built from
    ///     the aggregate, the text has both — which is the reason
    ///     <see cref="Neutral.VectorProjectionMap{TId}" /> exists rather than the map simply being
    ///     shared.
    /// </summary>
    [Fact]
    public async Task content_is_built_from_aggregate_state_across_a_partial_update()
    {
        using var daemon = await _store.BuildProjectionDaemonAsync();
        await daemon.StartAllAsync();

        await using var session = _store.LightweightSession();
        session.Events.StartStream("note-2", new NoteWritten("note-2", "a body"));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        session.Events.Append("note-2", new NoteTitled("note-2", "a title"));
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);

        await daemon.WaitForNonStaleData(TimeSpan.FromSeconds(30));

        await using var conn = session.Database.CreateConnection();
        await conn.OpenAsync(TestContext.Current.CancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select content_text from pgvector_aggregate_map.note_aggregate_vectors where id = $1";
        cmd.Parameters.Add(new Npgsql.NpgsqlParameter { Value = "note-2" });

        (await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe("a title a body");
    }
}
