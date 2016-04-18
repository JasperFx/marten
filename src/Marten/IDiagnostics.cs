using System.Data;
using System.Linq;
using Marten.Linq;
using Npgsql;

namespace Marten
{
    public interface IDiagnostics
    {

        /// <summary>
        /// Returns the dynamic C# code that will be generated for the document type. Useful to understand
        /// the internal behavior of Marten for a single document type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        string DocumentStorageCodeFor<T>();

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
    }
}