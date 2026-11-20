using System;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using EventSourcingTests.Projections;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests;

/// <summary>
/// #6051: <c>EnableStrictStreamIdentityEnforcement</c> only recorded a stream in <c>mt_streams_identity</c> when
/// <c>StartStream()</c> created it through the dedicated <c>mt_streams</c> insert. A stream created by appending to a
/// new id in a Quick append mode, and a stream that existed before the flag was turned on, had no identity row, so
/// their ids could be reused once they were archived under <c>UseArchivedStreamPartitioning</c>.
/// </summary>
public class strict_stream_identity_for_appended_and_older_streams: OneOffConfigurationsContext
{
    private void strict(EventAppendMode mode, StreamIdentity identity = StreamIdentity.AsGuid,
        TenancyStyle tenancy = TenancyStyle.Single)
    {
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = mode;
            opts.Events.StreamIdentity = identity;
            opts.Events.TenancyStyle = tenancy;
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
        });
    }

    private async Task<long> identityRowsFor(object id)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_streams_identity where id = @id";
        cmd.Parameters.AddWithValue("id", id);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private async Task archive(object id)
    {
        await using var session = theStore.LightweightSession();
        if (id is Guid guid) session.Events.ArchiveStream(guid);
        else session.Events.ArchiveStream((string)id);
        await session.SaveChangesAsync();
    }

    private async Task startingAgainIsRefused(object id)
    {
        await using var session = theStore.LightweightSession();
        if (id is Guid guid) session.Events.StartStream(guid, new MembersJoined());
        else session.Events.StartStream((string)id, new MembersJoined());

        await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () => await session.SaveChangesAsync());
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    [InlineData(EventAppendMode.Rich)]
    public async Task a_stream_created_by_append_is_recorded(EventAppendMode mode)
    {
        strict(mode);

        var stream = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        (await identityRowsFor(stream)).ShouldBe(1);

        await archive(stream);
        await startingAgainIsRefused(stream);
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task a_string_keyed_stream_created_by_append_is_recorded(EventAppendMode mode)
    {
        strict(mode, StreamIdentity.AsString);

        var stream = "appended-" + Guid.NewGuid().ToString("N");
        await using (var session = theStore.LightweightSession())
        {
            session.Events.Append(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        (await identityRowsFor(stream)).ShouldBe(1);

        await archive(stream);
        await startingAgainIsRefused(stream);
    }

    [Fact]
    public async Task a_conjoined_stream_created_by_append_is_recorded_for_its_tenant()
    {
        strict(EventAppendMode.Quick, tenancy: TenancyStyle.Conjoined);

        var stream = Guid.NewGuid();
        await using (var session = theStore.LightweightSession("one"))
        {
            session.Events.Append(stream, new MembersJoined());
            await session.SaveChangesAsync();
        }

        (await identityRowsFor(stream)).ShouldBe(1);

        await using (var session = theStore.LightweightSession("one"))
        {
            session.Events.ArchiveStream(stream);
            await session.SaveChangesAsync();
        }

        await using (var session = theStore.LightweightSession("one"))
        {
            session.Events.StartStream(stream, new MembersJoined());
            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () => await session.SaveChangesAsync());
        }
    }

    [Theory]
    [InlineData(EventAppendMode.Quick)]
    [InlineData(EventAppendMode.QuickWithServerTimestamps)]
    public async Task a_stream_created_by_fetch_for_writing_is_recorded(EventAppendMode mode)
    {
        strict(mode);

        var stream = Guid.NewGuid();
        await using (var session = theStore.LightweightSession())
        {
            var writing = await session.Events.FetchForWriting<QuestParty>(stream);
            writing.AppendOne(new MembersJoined());
            await session.SaveChangesAsync();
        }

        (await identityRowsFor(stream)).ShouldBe(1);

        await archive(stream);
        await startingAgainIsRefused(stream);
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid, TenancyStyle.Single)]
    [InlineData(StreamIdentity.AsString, TenancyStyle.Single)]
    [InlineData(StreamIdentity.AsGuid, TenancyStyle.Conjoined)]
    public async Task streams_that_predate_the_flag_are_recorded_when_the_identity_table_is_created(
        StreamIdentity identity, TenancyStyle tenancy)
    {
        // Created before the flag, the way an event store upgraded to 10.0 is
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = identity;
            opts.Events.TenancyStyle = tenancy;
            opts.Events.UseArchivedStreamPartitioning = false;
            opts.Events.EnableStrictStreamIdentityEnforcement = false;
        });

        object active = identity == StreamIdentity.AsGuid ? Guid.NewGuid() : "older-" + Guid.NewGuid().ToString("N");
        object archived = identity == StreamIdentity.AsGuid ? Guid.NewGuid() : "older-" + Guid.NewGuid().ToString("N");

        await using (var session = theStore.LightweightSession())
        {
            foreach (var id in new[] { active, archived })
            {
                if (id is Guid guid) session.Events.StartStream(guid, new MembersJoined());
                else session.Events.StartStream((string)id, new MembersJoined());
            }

            await session.SaveChangesAsync();
        }

        await archive(archived);

        // Then opened with the flags on, and the schema applied
        var schema = theStore.Options.DatabaseSchemaName;
        await using var upgraded = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.Events.StreamIdentity = identity;
            opts.Events.TenancyStyle = tenancy;
            opts.Events.UseArchivedStreamPartitioning = true;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;

            // Otherwise this store has no event store objects to apply
            opts.Events.AddEventType<MembersJoined>();
        });

        await upgraded.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        (await identityRowsFor(active)).ShouldBe(1);
        (await identityRowsFor(archived)).ShouldBe(1);

        // The schema converged: nothing left to apply
        await upgraded.Storage.Database.AssertDatabaseMatchesConfigurationAsync();

        await using (var session = upgraded.LightweightSession())
        {
            if (active is Guid guid) session.Events.ArchiveStream(guid);
            else session.Events.ArchiveStream((string)active);
            await session.SaveChangesAsync();
        }

        foreach (var id in new[] { active, archived })
        {
            await using var session = upgraded.LightweightSession();
            if (id is Guid guid) session.Events.StartStream(guid, new MembersJoined());
            else session.Events.StartStream((string)id, new MembersJoined());

            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () => await session.SaveChangesAsync());
        }
    }
}
