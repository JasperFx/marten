using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
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
        // TODO -- try/catch around this will stop or pause the shard or shard group
        return Source.GroupEvents(Store, Database, Range, token);
    }
}

// TODO -- this is hot garbage, replace when you can
internal static class BlockFactory
{
    public static ExecutionDataflowBlockOptions SequentialOptions(this CancellationToken token)
    {
        return new ExecutionDataflowBlockOptions
        {
            EnsureOrdered = true, MaxDegreeOfParallelism = 1, CancellationToken = token
        };
    }

    public static TransformBlock<EventRange, EventRangeGroup> BuildGrouping(IProjectionSource source,
        DocumentStore store, IMartenDatabase database, CancellationToken token)
    {
        var options = token.SequentialOptions();
        var pipeline = store.Options.ResiliencePipeline;

        // TODO -- build in communication to the parent in the case of failures getting out of the resilience
        // block
        Task<EventRangeGroup> Transform(EventRange range)
        {
            var execution = new GroupExecution(source, range, database, store);
            return pipeline.ExecuteAsync(static (x, t) => x.GroupAsync(t), execution, token).AsTask();
        }

        return new TransformBlock<EventRange, EventRangeGroup>((Func<EventRange, Task<EventRangeGroup>>)Transform,
            options);
    }
}

public interface ISubscriptionExecution: IAsyncDisposable
{
    ValueTask StopAsync();

    int InFlightCount { get; }
    void Enqueue(EventPage range, SubscriptionAgent subscriptionAgent);
}



public interface ISubscriptionAgent
{
    void Pause(TimeSpan time);
}

// TODO -- subsume ProjectionController here. Command should also have a "loaded" event
public class SubscriptionAgent: IAsyncDisposable
{
    private readonly AsyncOptions _options;
    private readonly IEventLoader _loader;
    private readonly ISubscriptionExecution _execution;
    public string Identifier { get; }
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ActionBlock<Command> _commandBlock;

    public SubscriptionAgent(string identifier, AsyncOptions options, IEventLoader loader, ISubscriptionExecution execution)
    {
        _options = options;
        _loader = loader;
        _execution = execution;
        Identifier = identifier;

        _commandBlock = new ActionBlock<Command>(Apply, _cancellation.Token.SequentialOptions());
    }

    public CancellationToken CancellationToken => _cancellation.Token;

    public long LastEnqueued { get; private set; }

    public long LastCommitted { get; private set; }

    public long HighWaterMark { get; private set; }

    public async ValueTask DisposeAsync()
    {
#if NET8_0_OR_GREATER
        await _cancellation.CancelAsync().ConfigureAwait(false);
        #else
        _cancellation.Cancel();
#endif
        _commandBlock.Complete();
        await _execution.DisposeAsync().ConfigureAwait(false);
    }

    internal void PostCommand(Command command) => _commandBlock.Post(command);

    internal async Task Apply(Command command)
    {
        if (_cancellation.IsCancellationRequested) return;

        switch (command.Type)
        {
            case CommandType.HighWater:
                MarkHighWater(command.HighWaterMark);
                break;

            case CommandType.Start:
                Start(command.HighWaterMark, command.LastCommitted);
                break;

            case CommandType.RangeCompleted:
                EventRangeUpdated(command.Range);
                break;
        }

    }

    public void MarkHighWater(long sequence)
    {
        // Ignore the high water mark if it's lower than
        // already encountered. Not sure how that could happen,
        // but still be ready for that.
        if (sequence <= HighWaterMark)
        {
            return;
        }

        HighWaterMark = sequence;

        enqueueNewEventRanges();
    }

    public void Start(long highWaterMark, long lastCommitted)
    {
        if (lastCommitted > highWaterMark)
        {
            throw new InvalidOperationException(
                $"The last committed number ({lastCommitted}) cannot be higher than the high water mark ({highWaterMark})");
        }

        HighWaterMark = highWaterMark;
        LastCommitted = LastEnqueued = lastCommitted;


        if (HighWaterMark > 0)
        {
            enqueueNewEventRanges();
        }
    }

    public void EventRangeUpdated(EventRange range)
    {
        LastCommitted = range.SequenceCeiling;

        enqueueNewEventRanges();
    }

    private void enqueueNewEventRanges()
    {
        /*
         * Logic:
         * Track how many batches are ongoing? Keep a minimum of 2 batches active in hopper
         * If there's more in the db than the batch size, build and post
         *
         *
         * if high water mark == last committed, do nothing
         * if high water mark == last fetched, no fetching
         * if there is more on deck than the maximum
         *
         *
         */


        while (HighWaterMark > LastEnqueued && _execution.InFlightCount < _options.MaximumHopperSize)
        {
            var floor = LastEnqueued;
            var ceiling = LastEnqueued + _options.BatchSize;
            if (ceiling > HighWaterMark)
            {
                ceiling = HighWaterMark;
            }

            startRange(floor, ceiling);
        }
    }

    private async Task loadNextAsync()
    {
        var request = new EventRequest
        {
            HighWater = HighWaterMark, BatchSize = _options.BatchSize, Floor = LastEnqueued
        };

        // TODO -- try/catch, and you pause here if this happens.
        var page = await _loader.LoadAsync(request, _cancellation.Token).ConfigureAwait(false);

        LastEnqueued = page.Ceiling;

        _execution.Enqueue(page, this);
    }

    private void startRange(long floor, long ceiling)
    {


        // var range = new EventRange(_shardName, floor, ceiling);
        // LastEnqueued = range.SequenceCeiling;
        // _inFlight.Enqueue(range);
        // _agent.StartRange(range);
    }
}
