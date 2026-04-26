using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// Companion analysis tests for https://github.com/JasperFx/marten/pull/4287
/// (issue #4286). The PR adds <c>mt_streams_default</c> to the partition-aware
/// <c>matches()</c> check so the unique-violation that PostgreSQL surfaces against
/// the partition is still translated into <see cref="ExistingStreamIdCollisionException"/>.
///
/// PR #4287 covers the easy case: starting a stream against an existing,
/// unarchived stream id. These tests cover the more subtle case the user asked
/// about — starting a new stream with the id of a stream that was previously
/// archived. Both stream-identity styles (Guid + string) are covered, with and
/// without <c>UseArchivedStreamPartitioning</c>.
///
/// Findings (see test names for the headline):
///
///   * Without partitioning, archive merely flips <c>is_archived</c> on the
///     existing row. The unique constraint on <c>mt_streams.id</c> still fires,
///     so re-using the id throws <see cref="ExistingStreamIdCollisionException"/>.
///     This is the "happy path" — Marten's documented contract holds.
///
///   * With <c>UseArchivedStreamPartitioning = true</c>, archive moves the row
///     to <c>mt_streams_archived</c>. The partition key (<c>is_archived</c>) is
///     part of the primary key, so a fresh row with <c>is_archived = false</c>
///     does not collide. Re-using an archived stream id currently SUCCEEDS,
///     which is inconsistent with the non-partitioned mode. This is the
///     "sad path" — the documented contract diverges by partitioning mode.
/// </summary>
public class start_stream_with_id_of_previously_archived_stream: OneOffConfigurationsContext
{
    // ─────────────────────────── happy path ───────────────────────────

    [Fact]
    public async Task happy_path__no_partitioning__guid__throws_when_starting_with_id_of_archived_stream()
    {
        // Default: UseArchivedStreamPartitioning = false. Archive only flips
        // is_archived on the same row, so the (id) PK on mt_streams still
        // collides on a second StartStream with the same id.
        StoreOptions(_ => { });

        var stream = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(stream);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(
                async () => await session.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task happy_path__no_partitioning__string_key__throws_when_starting_with_key_of_archived_stream()
    {
        StoreOptions(opts => opts.Events.StreamIdentity = StreamIdentity.AsString);

        var streamKey = $"order-{Guid.NewGuid():N}";

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamKey, new MembersJoined());
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamKey);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamKey, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(
                async () => await session.SaveChangesAsync());
        }
    }

    // ─────────────────────────── sad path ───────────────────────────

    /// <summary>
    /// Documents the current (and arguably surprising) behavior: with
    /// <c>UseArchivedStreamPartitioning = true</c>, Marten lets you start a new
    /// stream with the id of one that was previously archived. The archived
    /// events still live in the archived partition; the new stream is a
    /// completely fresh row in the default partition.
    /// </summary>
    [Fact]
    public async Task sad_path__partitioned__guid__id_of_archived_stream_can_be_reused()
    {
        StoreOptions(opts => opts.Events.UseArchivedStreamPartitioning = true);

        var stream = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined { Members = new[] { "alice" } });
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(stream);
            await session.SaveChangesAsync();
        }

        // The archived row physically lives in mt_streams_archived; mt_streams_default
        // has no row with this id any more, so the second StartStream's INSERT
        // produces no unique-key collision.
        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined { Members = new[] { "bob" } });
            await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
        }

        // Inspect the partitions directly to make the divergence concrete:
        //   - mt_streams_default has the freshly-started (unarchived) row
        //   - mt_streams_archived still has the original archived row
        await using var query = theStore.QuerySession();

        var defaultCount = (long)(await query.Connection
            .CreateCommand($"select count(*) from {SchemaName}.mt_streams_default where id = :id")
            .With("id", stream)
            .ExecuteScalarAsync())!;
        defaultCount.ShouldBe(1);

        var archivedCount = (long)(await query.Connection
            .CreateCommand($"select count(*) from {SchemaName}.mt_streams_archived where id = :id")
            .With("id", stream)
            .ExecuteScalarAsync())!;
        archivedCount.ShouldBe(1);
    }

    [Fact]
    public async Task sad_path__partitioned__string_key__id_of_archived_stream_can_be_reused()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseArchivedStreamPartitioning = true;
        });

        var streamKey = $"order-{Guid.NewGuid():N}";

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamKey, new MembersJoined { Members = new[] { "alice" } });
            await session.SaveChangesAsync();

            session.Events.ArchiveStream(streamKey);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(streamKey, new MembersJoined { Members = new[] { "bob" } });
            await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
        }

        await using var query = theStore.QuerySession();

        var defaultCount = (long)(await query.Connection
            .CreateCommand($"select count(*) from {SchemaName}.mt_streams_default where id = :id")
            .With("id", streamKey)
            .ExecuteScalarAsync())!;
        defaultCount.ShouldBe(1);

        var archivedCount = (long)(await query.Connection
            .CreateCommand($"select count(*) from {SchemaName}.mt_streams_archived where id = :id")
            .With("id", streamKey)
            .ExecuteScalarAsync())!;
        archivedCount.ShouldBe(1);
    }
}
