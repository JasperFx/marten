using System.Threading;
using System.Threading.Tasks;

namespace Marten.TestHelpers;

using System;
using System.Collections.Generic;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

public class OneOffConfigurationsHelper(string schemaName, string connectionString)
{
    private DocumentStore _store;
    protected IDocumentSession _session;
    protected readonly IList<IDisposable> _disposables = new List<IDisposable>();

    public string SchemaName => schemaName;

    public IList<IDisposable> Disposables => _disposables;

    public DocumentStore SeparateStore(Action<StoreOptions> configure = null)
    {
        var options = new StoreOptions { DatabaseSchemaName = SchemaName };

        options.Connection();

        configure?.Invoke(options);

        var store = new DocumentStore(options);

        _disposables.Add(store);

        return store;
    }

    public DocumentStore StoreOptions(Action<StoreOptions> configure, bool cleanAll = true)
    {
        var options = new StoreOptions();
        options.Connection(connectionString);

        // Can be overridden
        options.AutoCreateSchemaObjects = AutoCreate.All;
        options.NameDataLength = 100;
        options.DatabaseSchemaName = schemaName;

        configure(options);

        if (cleanAll)
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            conn.CreateCommand($"drop schema if exists {schemaName} cascade")
                .ExecuteNonQuery();
        }

        _store = new DocumentStore(options);

        _disposables.Add(_store);

        return _store;
    }

    public DocumentStore TheStore
    {
        get
        {
            if (_store == null)
            {
                StoreOptions(_ => { });
            }

            return _store;
        }
    }

    public IDocumentSession TheSession
    {
        get
        {
            if (_session != null)
                return _session;

            _session = TheStore.LightweightSession();
            _disposables.Add(_session);

            return _session;
        }
    }

    public Task AppendEvent(Guid streamId, params object[] events)
    {
        TheSession.Events.Append(streamId, events);
        return TheSession.SaveChangesAsync(CancellationToken.None);
    }
}
