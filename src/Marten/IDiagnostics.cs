#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Npgsql;

namespace Marten;

/// <summary>
///     Access to diagnostics about the current Marten IDocumentStore
/// </summary>
public interface IDiagnostics
{
    /// <summary>
    ///     Preview the database command that will be executed for this compiled query
    ///     object
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TReturn"></typeparam>
    /// <param name="query"></param>
    /// <returns></returns>
    NpgsqlCommand PreviewCommand<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query,
        DocumentTracking trackingMode = DocumentTracking.QueryOnly) where TDoc : notnull where TReturn : notnull;

    /// <summary>
    ///     Method to fetch Postgres server version
    /// </summary>
    /// <returns>Returns version</returns>
    Version GetPostgresVersion();

    /// <summary>
    ///     Find the Postgresql EXPLAIN PLAN for this compiled query
    /// </summary>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TReturn"></typeparam>
    /// <param name="query"></param>
    /// <returns></returns>
    Task<QueryPlan?> ExplainPlanAsync<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query,
        CancellationToken token = default) where TDoc : notnull where TReturn : notnull;
}
