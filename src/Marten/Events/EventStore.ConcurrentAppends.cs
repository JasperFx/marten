#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Exceptions;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events;

internal partial class EventStore
{
    public void BuildCommandForReadingVersionForStream(bool isGlobal, ICommandBuilder builder, Guid streamId,
        bool forUpdate)
    {
        builder.Append("select version from ");
        builder.Append(_store.Events.DatabaseSchemaName);
        builder.Append('.');
        builder.Append("mt_streams where id = ");
        builder.AppendParameter(streamId);

        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined && !isGlobal)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        if (forUpdate)
        {
            builder.Append(" for update");
        }
    }

    public void BuildCommandForReadingVersionForStream(bool isGlobal, ICommandBuilder builder, string streamKey,
        bool forUpdate)
    {
        builder.Append("select version from ");
        builder.Append(_store.Events.DatabaseSchemaName);
        builder.Append('.');
        builder.Append("mt_streams where id = ");
        builder.AppendParameter(streamKey);

        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined && !isGlobal)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        if (forUpdate)
        {
            builder.Append(" for update");
        }
    }


    // AppendOptimistic and AppendExclusive are EventOperationsBase's (jasperfx#1047); the version read they
    // need is readVersionForAppendAsync, in EventStore.Operations.cs

    private async Task<long> readVersionFromExistingStream(Guid streamId, bool forUpdate, CancellationToken token)
    {
        var builder = new CommandBuilder{TenantId = _session.TenantId};
        BuildCommandForReadingVersionForStream(false, builder, streamId, forUpdate);

        long version = 0;
        try
        {
            await using var reader = await _session.ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
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
        var builder = new CommandBuilder { TenantId = _session.TenantId };
        BuildCommandForReadingVersionForStream(false, builder, streamKey, forUpdate);

        long version = 0;
        try
        {
            await using var reader = await _session.ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
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
