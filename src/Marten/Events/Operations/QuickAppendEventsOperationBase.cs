using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;
using ICommandBuilder = Weasel.Postgresql.ICommandBuilder;

namespace Marten.Events.Operations;

public abstract class QuickAppendEventsOperationBase : IStorageOperation
{
    public QuickAppendEventsOperationBase(StreamAction stream)
    {
        Stream = stream;
    }

    public EventGraph Events { get; set; }

    public StreamAction Stream { get; }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }

    public Type DocumentType => typeof(IEvent);

    public override string ToString()
    {
        return $"Append {Stream.Events.Select(x => x.EventTypeName).Join(", ")} to event stream {Stream}";
    }

    public abstract void ConfigureCommand(ICommandBuilder builder, IMartenSession session);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (reader.Read())
        {
            var values = reader.GetFieldValue<long[]>(0);

            var finalVersion = values[0];
            foreach (var e in Stream.Events.Reverse())
            {
                e.Version = finalVersion;
                finalVersion--;
            }

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                Stream.Events[i - 1].Sequence = values[i];
            }

            if (Events is { UseMandatoryStreamTypeDeclaration: true } && Stream.Events[0].Version == 1)
            {
                throw new NonExistentStreamException(Events.StreamIdentity == StreamIdentity.AsGuid
                    ? Stream.Id
                    : Stream.Key);
            }
        }
    }

    protected void writeId(IGroupedParameterBuilder builder)
    {
        builder.AppendParameter(Stream.Id);
    }

    protected void writeKey(IGroupedParameterBuilder builder)
    {
        builder.AppendParameter(Stream.Key);
    }

    protected void writeBasicParameters(IGroupedParameterBuilder builder, IMartenSession session)
    {
        builder.AppendTextParameter(Stream.AggregateTypeName);
        builder.AppendTextParameter(Stream.TenantId);

        builder.AppendGuidArrayParameter(Stream.Events.Select(x => x.Id).ToArray());
        builder.AppendStringArrayParameter(Stream.Events.Select(x => x.EventTypeName).ToArray());
        builder.AppendStringArrayParameter(Stream.Events.Select(x => x.DotNetTypeName).ToArray());
        builder.AppendJsonArrayParameter(session.Serializer, Stream.Events.Select(x => x.Data).ToArray());
    }

    protected void writeCausationIds(IGroupedParameterBuilder builder)
    {
        builder.AppendStringArrayParameter(Stream.Events.Select(x => x.CausationId).ToArray());
    }

    protected void writeCorrelationIds(IGroupedParameterBuilder builder)
    {
        builder.AppendStringArrayParameter(Stream.Events.Select(x => x.CorrelationId).ToArray());
    }

    protected void writeHeaders(IGroupedParameterBuilder builder, IMartenSession session)
    {
        builder.AppendJsonArrayParameter(session.Serializer, Stream.Events.Select(x => x.Headers ?? new()).ToArray());
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var values = await reader.GetFieldValueAsync<long[]>(0, token).ConfigureAwait(false);

            var finalVersion = values[0];
            foreach (var e in Stream.Events.Reverse())
            {
                e.Version = finalVersion;
                finalVersion--;
            }

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                Stream.Events[i - 1].Sequence = values[i];
            }

            if (Events is { UseMandatoryStreamTypeDeclaration: true } && Stream.Events[0].Version == 1)
            {
                throw new NonExistentStreamException(Events.StreamIdentity == StreamIdentity.AsGuid
                    ? Stream.Id
                    : Stream.Key);
            }
        }
    }
}
