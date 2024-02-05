using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Daemon.New;

public interface IEventLoader
{
    Task<EventPage> LoadAsync(EventRequest request, CancellationToken token);
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

    public EventLoader(DocumentStore store, MartenDatabase database, AsyncProjectionShard shard, AsyncOptions options)
    {
        _store = store;
        _database = database;

        _storage = (IEventStorage)store.Options.Providers.StorageFor<IEvent>().QueryOnly;
        _batchSize = options.BatchSize;

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

        var filters = shard.BuildFilters(store);
        foreach (var filter in filters)
        {
            builder.Append(" and ");
            filter.Apply(builder);
        }

        builder.Append(" order by d.seq_id limit ");
        builder.Append(_batchSize);

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
