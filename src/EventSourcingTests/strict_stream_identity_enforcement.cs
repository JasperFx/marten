using System;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// Tests for <c>StoreOptions.Events.EnableStrictStreamIdentityEnforcement</c>.
///
/// The flag opts into a non-partitioned <c>mt_streams_identity</c> tracking
/// table whose primary key holds every stream identity ever written. Even
/// after a stream is archived (and physically moved to the archived partition
/// when <c>UseArchivedStreamPartitioning</c> is on), its row in
/// <c>mt_streams_identity</c> stays put. Re-using that identity therefore
/// surfaces a unique violation that <see cref="InsertStreamBase"/> translates
/// into <see cref="ExistingStreamIdCollisionException"/>.
///
/// Without the flag, that reuse silently succeeds under partitioning (see
/// <c>start_stream_with_id_of_previously_archived_stream</c> for the
/// documentation tests).
/// </summary>
public class strict_stream_identity_enforcement: OneOffConfigurationsContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task guid__start_archive_start_throws_when_strict_enforcement_enabled(bool usePartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
        });

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task string_key__start_archive_start_throws_when_strict_enforcement_enabled(bool usePartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
        });

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

    [Fact]
    public async Task strict_enforcement_still_throws_for_plain_duplicate_starts()
    {
        // Sanity check: the flag must not regress the non-archived collision case.
        StoreOptions(opts => opts.Events.EnableStrictStreamIdentityEnforcement = true);

        var stream = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(
                async () => await session.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task strict_enforcement_does_not_block_appending_to_an_existing_stream(bool usePartitioning)
    {
        // The identity-tracking row is only inserted on the first append (which
        // creates the mt_streams row). Subsequent Append calls must keep working.
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
        });

        var stream = Guid.NewGuid();

        await using (var session = theStore.LightweightSession())
        {
            session.Events.StartStream(stream, new MembersJoined());
            await session.SaveChangesAsync();

            // Plain appends must succeed against the existing row.
            session.Events.Append(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        await using var query = theStore.LightweightSession();
        var stateAfter = await query.Events.FetchStreamStateAsync(stream);
        stateAfter.Version.ShouldBe(2);
    }

    [Fact]
    public async Task default_behavior_unchanged__flag_off_allows_reuse_under_partitioning()
    {
        // Smoke test the contrast: flag OFF + partitioning → reuse succeeds
        // silently. This is the pre-existing behavior the flag exists to fix.
        StoreOptions(opts =>
        {
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.EnableStrictStreamIdentityEnforcement = false; // explicit
        });

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

            await Should.NotThrowAsync(async () => await session.SaveChangesAsync());
        }
    }
}
