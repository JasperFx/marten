using Baseline;
using Marten.Linq;
using Marten.Schema;
using Npgsql;

namespace Marten.Services
{
    public class Diagnostics : IDiagnostics
    {
        private readonly IDocumentSchema _schema;

        public Diagnostics(IDocumentSchema schema)
        {
            _schema = schema;
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
            var handler = _schema.HandlerFactory.HandlerFor(query, out stats);
            var cmd = new NpgsqlCommand();
            handler.ConfigureCommand(cmd);

            return cmd;
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

            using (var conn = new ManagedConnection(_schema.StoreOptions.ConnectionFactory()))
            {
                return conn.ExplainQuery(cmd);
            }
        }
    }
}