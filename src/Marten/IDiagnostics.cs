using System;
using Marten.Linq;
using Npgsql;
#nullable enable
namespace Marten
{
    /// <summary>
    /// Access to diagnostics about the current Marten IDocumentStore
    /// </summary>
    public interface IDiagnostics
    {
        /// <summary>
        /// Preview the database command that will be executed for this compiled query
        /// object
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        NpgsqlCommand PreviewCommand<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query);

        /// <summary>
        /// Find the Postgresql EXPLAIN PLAN for this compiled query
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        QueryPlan ExplainPlan<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query);

        /// <summary>
        /// Method to fetch Postgres server version
        /// </summary>
        /// <returns>Returns version</returns>
        Version GetPostgresVersion();
    }
}
