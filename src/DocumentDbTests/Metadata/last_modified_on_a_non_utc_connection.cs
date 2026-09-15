using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.LastModified;
using Marten.Patching;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

/// <summary>
/// #5379 (diagnosed in #5136). <c>mt_last_modified</c> is a <c>timestamp with time zone</c>, and the
/// UPDATE path stamped it with <c>now() at time zone 'utc'</c>. That expression <b>strips</b> the
/// offset to produce a naive timestamp holding UTC wall-clock time, and assigning a naive timestamp
/// back to a <c>timestamptz</c> column re-interprets it in the session's <c>TimeZone</c>. On a
/// database at UTC+2 the stored instant was two hours in the past — a genuinely different point in
/// time, not a display artifact.
///
/// <para>
/// The INSERT default was always correct (<c>transaction_timestamp()</c>), so a row was stamped right
/// and then <b>jumped by the offset the first time it was patched</b>. That is how the reporter hit
/// it: a migration written with <c>Patch&lt;T&gt;()</c>, and <c>ModifiedSince</c> queries that
/// silently stopped matching.
/// </para>
///
/// <para>
/// ⚠️ <b>Patching is the only way in.</b> <c>PatchOperation</c> is the sole caller of
/// <c>MetadataColumn.WriteMetadataInUpdateStatement</c>; an ordinary <c>Store</c>/<c>SaveChanges</c>
/// goes through the generated upsert and takes the column's DEFAULT, which was always right. That
/// bounds the blast radius, and <c>an_ordinary_update_was_never_affected</c> holds it.
/// </para>
///
/// <para>
/// ⚠️ <b>These tests pin a non-UTC <c>TimeZone</c> on the connection, and that is the entire point.</b>
/// At UTC the buggy and correct spellings are <b>byte-identical in effect</b>, so the whole existing
/// suite — and CI, and the dev container, all of which run at <c>Etc/UTC</c> — is structurally
/// incapable of seeing this class of bug. #5136 says so explicitly, and it is why that issue was
/// closed as "latent" before a user reported it from production.
/// </para>
/// </summary>
[Collection("metadata")]
public class last_modified_on_a_non_utc_connection: OneOffConfigurationsContext
{
    /// <summary>
    ///     Europe/Berlin is UTC+1 or +2 depending on the season. Either way it is non-zero, which is all
    ///     this needs — the assertions are about the size of the skew, not its exact value.
    /// </summary>
    private const string NonUtcTimeZone = "Europe/Berlin";

    public class Reading
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private DocumentStore aStoreOnANonUtcConnection()
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Timezone = NonUtcTimeZone
        };

        return DocumentStore.For(opts =>
        {
            opts.Connection(builder.ConnectionString);
            opts.DatabaseSchemaName = "last_modified_tz";
            opts.AutoCreateSchemaObjects = JasperFx.AutoCreate.All;
        });
    }

    /// <summary>
    ///     Guards the guard: if the connection is not actually at a non-UTC offset, every assertion below
    ///     passes against the bug and this file is worthless.
    /// </summary>
    [Fact]
    public async Task the_connection_really_is_not_at_utc()
    {
        using var store = aStoreOnANonUtcConnection();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var builder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Timezone = NonUtcTimeZone
        };

        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync(TestContext.Current.CancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "select extract(timezone from now())::int";
        var offsetSeconds = (int)(await cmd.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;

        offsetSeconds.ShouldNotBe(0);
    }

    /// <summary>
    ///     The reported defect: a patched document's <c>LastModified</c> is when it was patched, not the
    ///     UTC offset earlier.
    /// </summary>
    [Fact]
    public async Task patching_stamps_last_modified_at_the_real_instant()
    {
        using var store = aStoreOnANonUtcConnection();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Store(new Reading { Id = id, Value = 1 });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var before = DateTimeOffset.UtcNow;

        await using (var session = store.LightweightSession())
        {
            session.Patch<Reading>(id).Set(x => x.Value, 2);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = store.QuerySession();
        var metadata = await query.MetadataForAsync(
            new Reading { Id = id }, TestContext.Current.CancellationToken);

        metadata.ShouldNotBeNull();

        // ⚠️ A minute of tolerance, against a skew of at least an hour. Anything tighter would be a
        // flaky clock test; anything looser would stop distinguishing the bug from the fix.
        var skew = before - metadata.LastModified;
        skew.ShouldBeLessThan(TimeSpan.FromMinutes(1));
        metadata.LastModified.ShouldBeGreaterThan(before.AddMinutes(-1));
    }

    /// <summary>
    ///     The ordinary UPDATE path was never affected, and that bounds the blast radius.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>This one passes with or without the fix, deliberately, and saying so is the point.</b>
    ///         I first wrote it believing every document update ran the broken expression. It does not:
    ///         <c>PatchOperation</c> is the ONLY caller of
    ///         <c>MetadataColumn.WriteMetadataInUpdateStatement</c>, and an ordinary
    ///         <c>Store</c>/<c>SaveChanges</c> goes through the generated upsert, which stamps the column
    ///         from its DEFAULT — <c>transaction_timestamp()</c>, correct all along. The maintainer's own
    ///         correction on #5136 says the same; I only believed it after this test passed against the
    ///         unfixed code.
    ///     </para>
    ///     <para>
    ///         Kept because "which writes were wrong" is the first question anyone upgrading will ask,
    ///         and the answer — <b>only patching</b> — is worth holding rather than re-deriving.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task an_ordinary_update_was_never_affected()
    {
        using var store = aStoreOnANonUtcConnection();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Store(new Reading { Id = id, Value = 1 });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var before = DateTimeOffset.UtcNow;

        await using (var session = store.LightweightSession())
        {
            session.Store(new Reading { Id = id, Value = 2 });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = store.QuerySession();
        var metadata = await query.MetadataForAsync(
            new Reading { Id = id }, TestContext.Current.CancellationToken);

        metadata.ShouldNotBeNull();
        metadata.LastModified.ShouldBeGreaterThan(before.AddMinutes(-1));
    }

    /// <summary>
    ///     The consequence the reporter actually cared about: <c>ModifiedSince</c> finds a document that
    ///     was just modified.
    /// </summary>
    /// <remarks>
    ///     ⚠️ This is the fact that shows the defect was not cosmetic. With the stamp an hour or two in
    ///     the past, a query for "modified since a few seconds ago" silently returned nothing, and a
    ///     data migration looked like it had not run.
    /// </remarks>
    [Fact]
    public async Task modified_since_finds_a_document_that_was_just_patched()
    {
        using var store = aStoreOnANonUtcConnection();
        await store.Advanced.Clean.CompletelyRemoveAllAsync();
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var id = Guid.NewGuid();

        await using (var session = store.LightweightSession())
        {
            session.Store(new Reading { Id = id, Value = 1 });
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-5);

        await using (var session = store.LightweightSession())
        {
            session.Patch<Reading>(id).Set(x => x.Value, 2);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = store.QuerySession();
        var found = await query.Query<Reading>()
            .Where(x => x.ModifiedSince(cutoff))
            .ToListAsync(TestContext.Current.CancellationToken);

        found.Select(x => x.Id).ShouldContain(id);
    }
}
