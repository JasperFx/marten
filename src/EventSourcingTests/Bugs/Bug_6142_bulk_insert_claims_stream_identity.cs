using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

/// <summary>
/// #6142: under <c>EnableStrictStreamIdentityEnforcement</c> the bulk event import wrote <c>mt_streams</c> and
/// <c>mt_events</c> by COPY but never wrote the streams' <c>mt_streams_identity</c> rows. A bulk-imported stream
/// could then be archived and its id started again, which is what the flag exists to refuse. A stream started
/// through a session has always claimed its id; these tests hold the bulk paths to the same behaviour.
/// </summary>
public class Bug_6142_bulk_insert_claims_stream_identity: OneOffConfigurationsContext
{
    private const string Tenant = "one";

    private void strict(StreamIdentity identity, TenancyStyle tenancy, bool usePartitioning)
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = identity;
            opts.Events.TenancyStyle = tenancy;
            opts.Events.UseArchivedStreamPartitioning = usePartitioning;
            opts.Events.EnableStrictStreamIdentityEnforcement = true;
            opts.Events.AddEventType<MembersJoined>();
            opts.Events.AddEventType<MembersDeparted>();
        });
    }

    private static object newId(StreamIdentity identity) =>
        identity == StreamIdentity.AsGuid ? Guid.NewGuid() : "bulk-" + Guid.NewGuid().ToString("N");

    private StreamAction startAction(object id, params object[] events) =>
        id is Guid guid
            ? StreamAction.Start(theStore.Events, guid, events)
            : StreamAction.Start(theStore.Events, (string)id, events);

    private Task bulkInsert(TenancyStyle tenancy, IReadOnlyList<StreamAction> actions) =>
        tenancy == TenancyStyle.Conjoined
            ? theStore.BulkInsertEventsAsync(Tenant, actions)
            : theStore.BulkInsertEventsAsync(actions);

    private IDocumentSession session(TenancyStyle tenancy) =>
        tenancy == TenancyStyle.Conjoined ? theStore.LightweightSession(Tenant) : theStore.LightweightSession();

    private async Task<long> identityRowsFor(IEnumerable<object> ids)
    {
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();

        long count = 0;
        foreach (var id in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_streams_identity where id = @id";
            cmd.Parameters.AddWithValue("id", id);
            count += (long)(await cmd.ExecuteScalarAsync())!;
        }

        return count;
    }

    private static void append(IDocumentSession session, object id, params object[] events)
    {
        if (id is Guid guid) session.Events.Append(guid, events);
        else session.Events.Append((string)id, events);
    }

    private static void start(IDocumentSession session, object id, params object[] events)
    {
        if (id is Guid guid) session.Events.StartStream(guid, events);
        else session.Events.StartStream((string)id, events);
    }

    private static void archive(IDocumentSession session, object id)
    {
        if (id is Guid guid) session.Events.ArchiveStream(guid);
        else session.Events.ArchiveStream((string)id);
    }

    private static async Task<StreamState?> stateOf(IDocumentSession session, object id) =>
        id is Guid guid
            ? await session.Events.FetchStreamStateAsync(guid)
            : await session.Events.FetchStreamStateAsync((string)id);

    [Theory]
    [InlineData(StreamIdentity.AsGuid, TenancyStyle.Single, true)]
    [InlineData(StreamIdentity.AsString, TenancyStyle.Single, true)]
    [InlineData(StreamIdentity.AsGuid, TenancyStyle.Conjoined, true)]
    [InlineData(StreamIdentity.AsString, TenancyStyle.Conjoined, true)]
    [InlineData(StreamIdentity.AsGuid, TenancyStyle.Single, false)]
    [InlineData(StreamIdentity.AsString, TenancyStyle.Single, false)]
    public async Task bulk_inserted_streams_claim_their_identity_like_session_started_streams(
        StreamIdentity identity, TenancyStyle tenancy, bool usePartitioning)
    {
        strict(identity, tenancy, usePartitioning);

        var bulkIds = Enumerable.Range(0, 5).Select(_ => newId(identity)).ToList();
        await bulkInsert(tenancy,
            bulkIds.Select(id => startAction(id, new MembersJoined(), new MembersJoined())).ToList());

        // The same thing a session start leaves behind: one identity row per stream
        var sessionId = newId(identity);
        await using (var s = session(tenancy))
        {
            start(s, sessionId, new MembersJoined());
            await s.SaveChangesAsync();
        }

        (await identityRowsFor([sessionId])).ShouldBe(1);
        (await identityRowsFor(bulkIds)).ShouldBe(bulkIds.Count);

        var bulkId = bulkIds[0];

        // Appending to a bulk-imported stream works and claims nothing new
        await using (var s = session(tenancy))
        {
            append(s, bulkId, new MembersDeparted());
            await s.SaveChangesAsync();
        }

        await using (var s = session(tenancy))
        {
            (await stateOf(s, bulkId))!.Version.ShouldBe(3);
        }

        (await identityRowsFor([bulkId])).ShouldBe(1);

        // Starting an active bulk-imported stream again is refused, as it is for a session-started one
        foreach (var id in new[] { sessionId, bulkId })
        {
            await using var s = session(tenancy);
            start(s, id, new MembersJoined());
            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () => await s.SaveChangesAsync());
        }

        // And so is starting one that has been archived
        foreach (var id in new[] { sessionId, bulkId })
        {
            await using (var s = session(tenancy))
            {
                archive(s, id);
                await s.SaveChangesAsync();
            }

            await using (var s = session(tenancy))
            {
                start(s, id, new MembersJoined());
                await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () => await s.SaveChangesAsync());
            }
        }
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task bulk_insert_cannot_reuse_the_id_of_an_archived_stream(StreamIdentity identity)
    {
        strict(identity, TenancyStyle.Single, true);

        var id = newId(identity);
        await using (var s = theStore.LightweightSession())
        {
            start(s, id, new MembersJoined());
            await s.SaveChangesAsync();

            archive(s, id);
            await s.SaveChangesAsync();
        }

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            theStore.BulkInsertEventsAsync([startAction(id, new MembersJoined())]));
        ex.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        ex.ConstraintName.ShouldBe("pkey_mt_streams_identity_id");

        // The import is one transaction, so none of it was written
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            $"select count(*) from {theStore.Options.Events.DatabaseSchemaName}.mt_streams where id = @id and is_archived = false";
        cmd.Parameters.AddWithValue("id", id);
        ((long)(await cmd.ExecuteScalarAsync())!).ShouldBe(0);
    }

    [Theory]
    [InlineData(StreamIdentity.AsGuid)]
    [InlineData(StreamIdentity.AsString)]
    public async Task streamed_bulk_import_claims_identity_for_active_and_archived_streams(StreamIdentity identity)
    {
        strict(identity, TenancyStyle.Single, true);

        // The streamed import does no schema work; provisioning is the caller's job
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var active = newId(identity);
        var archived = newId(identity);

        var headers = new[] { header(active, false), header(archived, true) };
        var events = new[]
        {
            eventFor(active, 1, false), eventFor(archived, 1, true), eventFor(active, 2, false),
            eventFor(archived, 2, true)
        };

        await theStore.BulkInsertEventStreamAsync(StorageConstants.DefaultTenantId, headers, streamOf(events));

        (await identityRowsFor([active, archived])).ShouldBe(2);

        await using (var s = theStore.LightweightSession())
        {
            archive(s, active);
            await s.SaveChangesAsync();
        }

        foreach (var id in new[] { active, archived })
        {
            await using var s = theStore.LightweightSession();
            start(s, id, new MembersJoined());
            await Should.ThrowAsync<ExistingStreamIdCollisionException>(async () => await s.SaveChangesAsync());
        }
    }

    private static BulkEventStreamHeader header(object id, bool isArchived) =>
        id is Guid guid
            ? new BulkEventStreamHeader { Id = guid, Version = 2, IsArchived = isArchived }
            : new BulkEventStreamHeader { Key = (string)id, Version = 2, IsArchived = isArchived };

    private static IEvent eventFor(object id, long version, bool isArchived)
    {
        var e = new Event<MembersJoined>(new MembersJoined())
        {
            Version = version, Id = Guid.NewGuid(), IsArchived = isArchived
        };
        if (id is Guid guid) e.StreamId = guid;
        else e.StreamKey = (string)id;
        return e;
    }

    private static async IAsyncEnumerable<IEvent> streamOf(IEnumerable<IEvent> events)
    {
        foreach (var e in events)
        {
            yield return e;
        }

        await Task.CompletedTask;
    }
}
