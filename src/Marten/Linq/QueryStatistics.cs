namespace Marten.Linq
{
    /// <summary>
    /// Used to supply the total number of rows in the database for server side
    /// paging scenarios
    /// </summary>
    public class QueryStatistics
    {
        /// <summary>
        /// The total number of records in the database for this query
        /// </summary>
        public long TotalResults { get; set; }
    }
}
