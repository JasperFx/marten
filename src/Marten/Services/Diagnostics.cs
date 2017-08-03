using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Util;
using Npgsql;

namespace Marten.Services
{
    public class Diagnostics : IDiagnostics
    {
        private readonly DocumentStore _store;

        public Diagnostics(DocumentStore store)
        {
            _store = store;
        }


        /// <summary>
        /// Preview the database command that will be executed for this compiled query
        /// object
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        public NpgsqlCommand PreviewCommand<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query)
        {
            QueryStatistics stats;
            var handler = _store.HandlerFactory.HandlerFor(query, out stats);

            return CommandBuilder.ToCommand(_store.Tenancy.Default, handler);
        }

        /// <summary>
        /// Find the Postgresql EXPLAIN PLAN for this compiled query
        /// </summary>
        /// <typeparam name="TDoc"></typeparam>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="query"></param>
        /// <returns></returns>
        public QueryPlan ExplainPlan<TDoc, TReturn>(ICompiledQuery<TDoc, TReturn> query)
        {
            var cmd = PreviewCommand(query);

            using (var conn = _store.Tenancy.Default.OpenConnection())
            {
                return conn.ExplainQuery(cmd);
            }
        }
    }
}