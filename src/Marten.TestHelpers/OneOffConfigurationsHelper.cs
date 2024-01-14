using System.Threading;
using System.Threading.Tasks;

namespace Marten.TestHelpers;

using System;
using System.Collections.Generic;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;

public class OneOffConfigurationsHelper(string schemaName, string connectionString) : IDisposable
{
    private DocumentStore _store;
    protected IDocumentSession Session;

    public string SchemaName => schemaName;

    public IList<IDisposable> Disposables { get; } = new List<IDisposable>();

    public DocumentStore SeparateStore(Action<StoreOptions> configure = null)
    {
        var options = new StoreOptions { DatabaseSchemaName = SchemaName };

        options.Connection(connectionString);

        configure?.Invoke(options);

        var store = new DocumentStore(options);

        Disposables.Add(store);

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

        Disposables.Add(_store);

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
            if (Session != null)
                return Session;

            Session = TheStore.LightweightSession();
            Disposables.Add(Session);

            return Session;
        }
    }

    public Task AppendEvent(Guid streamId, params object[] events)
    {
        TheSession.Events.Append(streamId, events);
        return TheSession.SaveChangesAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        foreach (var disposable in Disposables)
        {
            disposable.Dispose();
        }
    }
}
