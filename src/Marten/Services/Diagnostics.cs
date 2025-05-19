#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;
using Marten.Linq;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Services;

public class Diagnostics: IDiagnostics
{
    private readonly DocumentStore _store;
    private Version? _postgreSqlVersion;

    public Diagnostics(DocumentStore store)
    {
        _store = store;
    }

    /// <summary>
    ///     Preview the database command that will be executed for this compiled query
    ///     object
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TReturn"></typeparam>
    /// <param name="query"></param>
    /// <returns></returns>
    public NpgsqlCommand PreviewCommand<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query,
        DocumentTracking trackingMode = DocumentTracking.QueryOnly) where TDoc : notnull where TReturn : notnull
    {
        using var session = OpenQuerySession(trackingMode);
        var source = _store.GetCompiledQuerySourceFor(query, session);
        var handler = source.Build(query, session);

        var command = new NpgsqlCommand();
        var builder = new CommandBuilder(command);
        handler.ConfigureCommand(builder, session);

        command.CommandText = builder.ToString();

        return command;
    }

    /// <summary>
    ///     Find the Postgresql EXPLAIN PLAN for this compiled query
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TReturn"></typeparam>
    /// <param name="query"></param>
    /// <returns></returns>
    public async Task<QueryPlan?> ExplainPlanAsync<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query, CancellationToken token = default) where TDoc : notnull where TReturn : notnull
    {
        var cmd = PreviewCommand(query);

        await using var conn = _store.Tenancy.Default.Database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);
        return await conn.ExplainQueryAsync(_store.Serializer, cmd, token: token).ConfigureAwait(false);
    }

    /// <summary>
    ///     Method to fetch Postgres server version
    /// </summary>
    /// <returns>Returns version</returns>
    public Version GetPostgresVersion()
    {
        if (_postgreSqlVersion != null)
        {
            return _postgreSqlVersion;
        }

        using var conn = _store.Tenancy.Default.Database.CreateConnection();
        conn.Open();

        _postgreSqlVersion = conn.PostgreSqlVersion;

        return _postgreSqlVersion;
    }

    private QuerySession OpenQuerySession(DocumentTracking tracking)
    {
        switch (tracking)
        {
            case DocumentTracking.None:
                return (QuerySession)_store.LightweightSession();
            case DocumentTracking.QueryOnly:
                return (QuerySession)_store.QuerySession();
            case DocumentTracking.IdentityOnly:
                return (QuerySession)_store.IdentitySession();
            case DocumentTracking.DirtyTracking:
                return (QuerySession)_store.DirtyTrackedSession();
        }

        throw new ArgumentOutOfRangeException(nameof(tracking));
    }
}
