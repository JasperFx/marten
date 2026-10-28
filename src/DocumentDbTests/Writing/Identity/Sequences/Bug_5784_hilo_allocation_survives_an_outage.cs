using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

public sealed class NumberedDocument
{
    public int Id { get; set; }
}

/// <summary>
/// #5784, adapted from the reporter's reproduction: the first HiLo id allocation on a cold database ran
/// into an outage, and the <see cref="Lazy{T}" /> behind <c>MartenDatabase.Sequences</c> cached that
/// connection failure. After the database came back on the same endpoint, ordinary queries on the store
/// recovered but every new-id allocation rethrew the <b>same exception object</b>, until the store was
/// replaced.
///
/// <para>
/// The reporter stopped and restarted a Testcontainers PostgreSQL on a fixed port. This takes the same
/// shape through a proxy that refuses connections while it is down, so the test can share the harness
/// database: the schema is provisioned by a separate store first, so neither missing schema nor a changed
/// endpoint can explain the failure, and the runtime store is cold when the outage begins.
/// </para>
/// </summary>
public class Bug_5784_hilo_allocation_survives_an_outage
{
    [Theory]
    [InlineData(AutoCreate.CreateOrUpdate)]
    [InlineData(AutoCreate.None)]
    public async Task a_new_id_can_be_allocated_once_the_database_is_back(AutoCreate mode)
    {
        var target = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);
        await using var proxy = SwitchableTcpProxy.Start(target.Host!, target.Port);

        var connection = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString)
        {
            Host = "127.0.0.1", Port = proxy.Port, Timeout = 3, CommandTimeout = 10, Pooling = false
        }.ConnectionString;

        var schema = "bug_5784_" + mode.ToString().ToLowerInvariant();

        DocumentStore open(AutoCreate autoCreate) => DocumentStore.For(opts =>
        {
            opts.Connection(connection);
            opts.DatabaseSchemaName = schema;
            opts.AutoCreateSchemaObjects = autoCreate;
            opts.Schema.For<NumberedDocument>();
        });

        // Provision before constructing the runtime store, so its sequence cache is cold
        await using (var migrator = open(AutoCreate.CreateOrUpdate))
        {
            await migrator.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
            await migrator.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(NumberedDocument));
        }

        await using var store = open(mode);

        proxy.GoDown();
        try
        {
            allocationFailure(store).ShouldBeAssignableTo<NpgsqlException>();
        }
        finally
        {
            proxy.ComeBackUp();
        }

        // Reads were never the problem
        await using (var query = store.QuerySession())
        {
            (await query.Query<NumberedDocument>().CountAsync()).ShouldBe(0);
        }

        // The defect: this rethrew the exception cached during the outage
        var created = new NumberedDocument();
        await using (var session = store.LightweightSession())
        {
            session.Store(created);
            await session.SaveChangesAsync();
        }

        created.Id.ShouldBeGreaterThan(0);

        await using (var query = store.QuerySession())
        {
            (await query.LoadAsync<NumberedDocument>(created.Id)).ShouldNotBeNull();
        }

        // Recovery still publishes exactly one factory, and every later caller shares it
        store.Storage.Database.Sequences.ShouldBeSameAs(store.Storage.Database.Sequences);
    }

    private static Exception allocationFailure(IDocumentStore store)
    {
        using var session = store.LightweightSession();
        return Should.Throw<Exception>(() => session.Store(new NumberedDocument()));
    }
}

/// <summary>
/// A TCP proxy that can go down and come back up on the same port, which is what a stopped and restarted
/// database looks like from the client. While it is down every connection is accepted and closed at once
/// and the connections already relayed are dropped.
/// </summary>
internal sealed class SwitchableTcpProxy: IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly string _targetHost;
    private readonly int _targetPort;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentDictionary<TcpClient, bool> _relayed = new();
    private volatile bool _down;

    private SwitchableTcpProxy(string targetHost, int targetPort)
    {
        _targetHost = targetHost;
        _targetPort = targetPort;
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public static SwitchableTcpProxy Start(string targetHost, int targetPort)
    {
        var proxy = new SwitchableTcpProxy(targetHost, targetPort);
        proxy._listener.Start();
        _ = proxy.acceptAsync();
        return proxy;
    }

    public void GoDown()
    {
        _down = true;
        foreach (var client in _relayed.Keys) client.Dispose();
    }

    public void ComeBackUp() => _down = false;

    private async Task acceptAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (Exception)
            {
                return;
            }

            if (_down)
            {
                client.Dispose();
                continue;
            }

            _ = relayAsync(client);
        }
    }

    private async Task relayAsync(TcpClient client)
    {
        using var server = new TcpClient();
        _relayed[client] = true;
        _relayed[server] = true;

        try
        {
            await server.ConnectAsync(_targetHost, _targetPort, _cancellation.Token);

            var fromClient = client.GetStream().CopyToAsync(server.GetStream(), _cancellation.Token);
            var fromServer = server.GetStream().CopyToAsync(client.GetStream(), _cancellation.Token);
            await Task.WhenAny(fromClient, fromServer);
        }
        catch (Exception)
        {
            // Either side went away, or the proxy went down or is being disposed
        }
        finally
        {
            _relayed.TryRemove(client, out _);
            _relayed.TryRemove(server, out _);
            client.Dispose();
        }
    }

    public ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();
        foreach (var client in _relayed.Keys) client.Dispose();
        _cancellation.Dispose();
        return ValueTask.CompletedTask;
    }
}
