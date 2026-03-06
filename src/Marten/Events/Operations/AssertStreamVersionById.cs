using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

internal class AssertStreamVersionById: IStorageOperation
{
    private readonly EventGraph _events;

    public AssertStreamVersionById(EventGraph events, StreamAction stream)
    {
        _events = events;
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        builder.Append("select version from ");
        builder.Append(_events.DatabaseSchemaName);
        builder.Append(".mt_streams where id = ");
        builder.AppendParameter(Stream.Id);
    }

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (!reader.Read())
        {
            var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Id, Stream.AggregateType,
                Stream.ExpectedVersionOnServer!.Value, 0);
            exceptions.Add(ex);
            return;
        }

        var actualVersion = reader.GetInt64(0);
        if (actualVersion != Stream.ExpectedVersionOnServer!.Value)
        {
            var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Id, Stream.AggregateType,
                Stream.ExpectedVersionOnServer.Value, actualVersion);
            exceptions.Add(ex);
        }
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Id, Stream.AggregateType,
                Stream.ExpectedVersionOnServer!.Value, 0);
            exceptions.Add(ex);
            return;
        }

        var actualVersion = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
        if (actualVersion != Stream.ExpectedVersionOnServer!.Value)
        {
            var ex = new EventStreamUnexpectedMaxEventIdException(Stream.Id, Stream.AggregateType,
                Stream.ExpectedVersionOnServer.Value, actualVersion);
            exceptions.Add(ex);
        }
    }

    public OperationRole Role() => OperationRole.Events;
}
