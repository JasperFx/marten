namespace Marten.Events.Projections.Async
{
    public static class ProjectionExtensions
    {
        /// <summary>
        /// "Page" size of events that expresses a maximum count to fetch in the async daemon.
        /// Default is 100.
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="pageSize"></param>
        /// <returns></returns>
        public static IProjection PageSize(this IProjection projection, int pageSize)
        {
            projection.AsyncOptions.PageSize = pageSize;
            return projection;
        }

        /// <summary>
        /// Maximum number of events to buffer in memory before pausing the fetching.
        /// The default is 1000
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static IProjection MaximumStagedEventCount(this IProjection projection, int count)
        {
            projection.AsyncOptions.MaximumStagedEventCount = count;
            return projection;
        }

        /// <summary>
        /// Lower threshold of staged events in memory for a projection before
        /// the Async Daemon resumes fetching. The default is
        /// 500
        /// </summary>
        /// <param name="projection"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static IProjection CooldownStagedEventCount(this IProjection projection, int count)
        {
            projection.AsyncOptions.CooldownStagedEventCount = count;
            return projection;
        }
    }
}