namespace Marten.Linq.Compiled
{
    internal interface IQueryStatisticsFinder
    {
        QueryStatistics Find(object query);
    }
}