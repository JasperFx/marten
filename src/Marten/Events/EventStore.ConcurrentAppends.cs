#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Storage;
using Npgsql;
using Weasel.Core;

namespace Marten.Events;

internal partial class EventStore
{
    public NpgsqlCommand BuildCommandForReadingVersionForStream(Guid streamId, bool forUpdate)
    {
        var cmd = _store.Events.TenancyStyle switch
        {
            TenancyStyle.Conjoined => new NpgsqlCommand(
                    $"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id and tenant_id = :tenant_id")
                .With("id", streamId)
                .With("tenant_id", _session.TenantId),
            _ => new NpgsqlCommand(
                    $"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id")
                .With("id", streamId)
        };

        if (forUpdate)
        {
            cmd.CommandText += " for update";
        }

        return cmd;
    }

    public NpgsqlCommand BuildCommandForReadingVersionForStream(string streamKey, bool forUpdate)
    {
        var cmd = _store.Events.TenancyStyle switch
        {
            TenancyStyle.Conjoined => new NpgsqlCommand(
                    $"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id and tenant_id = :tenant_id")
                .With("id", streamKey)
                .With("tenant_id", _session.TenantId),
            _ => new NpgsqlCommand(
                    $"select version from {_store.Events.DatabaseSchemaName}.mt_streams where id = :id")
                .With("id", streamKey)
        };

        if (forUpdate)
        {
            cmd.CommandText += " for update";
        }

        return cmd;
    }


    public async Task AppendOptimistic(string streamKey, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsStringStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var version = await readVersionFromExistingStream(streamKey, false, token).ConfigureAwait(false);

        var action = Append(streamKey, events);
        action.ExpectedVersionOnServer = version;
    }

    public Task AppendOptimistic(string streamKey, params object[] events)
    {
        return AppendOptimistic(streamKey, CancellationToken.None, events);
    }

    public async Task AppendOptimistic(Guid streamId, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsGuidStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var version = await readVersionFromExistingStream(streamId, false, token).ConfigureAwait(false);

        var action = Append(streamId, events);
        action.ExpectedVersionOnServer = version;
    }

    public Task AppendOptimistic(Guid streamId, params object[] events)
    {
        return AppendOptimistic(streamId, CancellationToken.None, events);
    }

    public async Task AppendExclusive(string streamKey, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsStringStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await _session.BeginTransactionAsync(token).ConfigureAwait(false);

        var version = await readVersionFromExistingStream(streamKey, true, token).ConfigureAwait(false);

        var action = Append(streamKey, events);
        action.ExpectedVersionOnServer = version;
    }

    public Task AppendExclusive(string streamKey, params object[] events)
    {
        return AppendExclusive(streamKey, CancellationToken.None, events);
    }

    public async Task AppendExclusive(Guid streamId, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsGuidStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        await _session.BeginTransactionAsync(token).ConfigureAwait(false);

        var version = await readVersionFromExistingStream(streamId, true, token).ConfigureAwait(false);

        var action = Append(streamId, events);
        action.ExpectedVersionOnServer = version;
    }

    public Task AppendExclusive(Guid streamId, params object[] events)
    {
        return AppendExclusive(streamId, CancellationToken.None, events);
    }

    private async Task<long> readVersionFromExistingStream(Guid streamId, bool forUpdate, CancellationToken token)
    {
        var cmd = BuildCommandForReadingVersionForStream(streamId, forUpdate);

        long version = 0;
        try
        {
            await using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(streamId, e.InnerException);
            }

            throw;
        }

        if (version == 0)
        {
            throw new NonExistentStreamException(streamId);
        }

        return version;
    }

    private async Task<long> readVersionFromExistingStream(string streamKey, bool forUpdate,
        CancellationToken token)
    {
        var cmd = BuildCommandForReadingVersionForStream(streamKey, forUpdate);

        long version = 0;
        try
        {
            await using var reader = await _session.ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(streamKey, e.InnerException);
            }

            throw;
        }

        if (version == 0)
        {
            throw new NonExistentStreamException(streamKey);
        }

        return version;
    }
}
