using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Polly;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Daemon.New;

public interface IDeadLetterQueue
{
    void Handle(EventDeserializationFailureException exception);
}

public class EventSelection
{
    public List<Type> EventTypes { get; } = new();
    public List<Type> AggregateTypes { get; } = new();
    public bool UseArchived { get; set; }
    public int BatchSize { get; set; } = 1000;

    // TODO -- might be an opportunity for the new strategy for event naming
    public IEnumerable<ISqlFragment> CreateFragments()
    {
        throw new NotImplementedException();
    }
}

public class EventPage(long Floor): List<IEvent>
{
    public long Ceiling { get; private set; }

    public void CalculateCeiling(int batchSize, long highWaterMark)
    {
        Ceiling = Count == batchSize
            ? this.Last().Sequence
            : highWaterMark;
    }

    internal IStorageOperation BuildProgressionOperation(EventGraph events)
    {
        throw new NotImplementedException();
        // if (Floor == 0)
        // {
        //     return new InsertProjectionProgress(events, this);
        // }
        //
        // return new UpdateProjectionProgress(events, this);
    }

    public void SkipEventSequence(long eventSequence)
    {
        RemoveAll(e => e.Sequence == eventSequence);
    }
}

public class EventRequest
{
    public long Floor { get; init; }
    public long HighWater { get; init; }
    public int BatchSize { get; init; }

    public override string ToString()
    {
        return $"{nameof(Floor)}: {Floor}, {nameof(HighWater)}: {HighWater}, {nameof(BatchSize)}: {BatchSize}";
    }

    protected bool Equals(EventRequest other)
    {
        return Floor == other.Floor && HighWater == other.HighWater && BatchSize == other.BatchSize;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != this.GetType())
        {
            return false;
        }

        return Equals((EventRequest)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Floor, HighWater, BatchSize);
    }
}

public interface IEventLoader
{
    Task<EventPage> LoadAsync(EventRequest request, CancellationToken token);
}


// TODO -- throw a bunch of logging in here too maybe
internal class ResilientEventLoader: IEventLoader
{
    private readonly ResiliencePipeline _pipeline;
    private readonly IEventLoader _inner;

    internal record EventLoadExecution(EventRequest Request, IEventLoader Loader)
    {
        public async ValueTask<EventPage> ExecuteAsync(CancellationToken token)
        {
            var results = await Loader.LoadAsync(Request, token).ConfigureAwait(false);
            return results;
        }
    }

    public ResilientEventLoader(ResiliencePipeline pipeline, IEventLoader inner)
    {
        _pipeline = pipeline;
        _inner = inner;
    }

    public Task<EventPage> LoadAsync(EventRequest request, CancellationToken token)
    {
        var execution = new EventLoadExecution(request, _inner);
        return _pipeline.ExecuteAsync<EventPage, EventLoadExecution>(static (x, t) => x.ExecuteAsync(t),
            execution, token).AsTask();
    }
}

internal class EventLoader: IEventLoader
{
    private readonly int _aggregateIndex;
    private readonly int _batchSize;
    private readonly NpgsqlParameter _ceiling;
    private readonly NpgsqlCommand _command;
    private readonly IMartenDatabase _database;
    private readonly NpgsqlParameter _floor;
    private readonly IEventStorage _storage;
    private readonly IDocumentStore _store;

    public EventLoader(DocumentStore store, MartenDatabase database, EventSelection selection)
    {
        _store = store;
        _database = database;

        _storage = (IEventStorage)store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        _batchSize = selection.BatchSize;

        var schemaName = store.Options.Events.DatabaseSchemaName;

        var builder = new CommandBuilder();
        builder.Append($"select {_storage.SelectFields().Select(x => "d." + x).Join(", ")}, s.type as stream_type");
        builder.Append(
            $" from {schemaName}.mt_events as d inner join {schemaName}.mt_streams as s on d.stream_id = s.id");

        if (_store.Options.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = s.tenant_id");
        }

        var parameters = builder.AppendWithParameters(" where d.seq_id > ? and d.seq_id <= ?");
        _floor = parameters[0];
        _ceiling = parameters[1];
        _floor.NpgsqlDbType = _ceiling.NpgsqlDbType = NpgsqlDbType.Bigint;

        var filters = selection.CreateFragments();
        foreach (var filter in filters)
        {
            builder.Append(" and ");
            filter.Apply(builder);
        }

        builder.Append(" order by d.seq_id take ");
        builder.Append(selection.BatchSize);

        _command = builder.Compile();
        _aggregateIndex = _storage.SelectFields().Length;
    }

    public async Task<EventPage> LoadAsync(EventRequest request, CancellationToken token)
    {
        // There's an assumption here that this method is only called sequentially
        // and never at the same time on the same instance
        var page = new EventPage(request.Floor);

        await using var session = (QuerySession)_store.QuerySession(SessionOptions.ForDatabase(_database));
        _floor.Value = request.Floor;
        _ceiling.Value = request.HighWater;

        await using var reader = await session.ExecuteReaderAsync(_command, token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            // TODO -- put error handling and dead letter queue around this! maybe by wrapping the IEventStorage
            // as a decorator
            var @event = await _storage.ResolveAsync(reader, token).ConfigureAwait(false);

            if (!await reader.IsDBNullAsync(_aggregateIndex, token).ConfigureAwait(false))
            {
                @event.AggregateTypeName =
                    await reader.GetFieldValueAsync<string>(_aggregateIndex, token).ConfigureAwait(false);
            }

            page.Add(@event);
        }

        page.CalculateCeiling(_batchSize, request.HighWater);

        return page;
    }
}

internal record GroupExecution(
    IProjectionSource Source,
    EventRange Range,
    IMartenDatabase Database,
    DocumentStore Store)
{
    public ValueTask<EventRangeGroup> GroupAsync(CancellationToken token)
    {
        return Source.GroupEvents(Store, Database, Range, token);
    }
}

public interface ISubscriptionExecution: IAsyncDisposable
{
    ValueTask StopAsync();

    void Enqueue(EventPage range, SubscriptionAgent subscriptionAgent);
}

public interface ISubscriptionAgent
{
    void Pause(TimeSpan time);
}
