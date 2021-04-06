#nullable enable
namespace Marten.Linq
{
    public interface IConfigureExplainExpressions
    {
        // Borrowing from PG 9.6 documentation https://www.postgresql.org/docs/9.2/static/sql-explain.html

        /// <summary>
        /// Carry out the command and show actual run times and other statistics.
        /// </summary>
        IConfigureExplainExpressions Analyze();

        /// <summary>
        /// Display additional information regarding the plan. Specifically, include the output column list for each node in the plan tree, schema-qualify table and function names, always label variables in expressions with their range table alias, and always print the name of each trigger for which statistics are displayed.
        /// </summary>
        IConfigureExplainExpressions Verbose();

        /// <summary>
        /// Include information on the estimated startup and total cost of each plan node, as well as the estimated number of rows and the estimated width of each row.
        /// </summary>
        IConfigureExplainExpressions Costs();

        /// <summary>
        /// Include information on buffer usage. Specifically, include the number of shared blocks hit, read, dirtied, and written, the number of local blocks hit, read, dirtied, and written, and the number of temp blocks read and written.
        /// </summary>
        IConfigureExplainExpressions Buffers();

        /// <summary>
        /// Include the actual startup time and time spent in the node in the output.
        /// </summary>
        IConfigureExplainExpressions Timing();
    }
}
