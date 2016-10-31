namespace Marten.Linq.QueryHandlers
{
    internal class LinqConstants
    {
        internal static readonly string StatsColumn = "count(1) OVER() as total_rows";
    }
}