namespace Marten.Events.Projections.Async
{
    public class AsyncOptions
    {
        /// <summary>
        /// "Page" size of events that expresses a maximum count to fetch in the async daemon.
        /// Default is 100.
        /// </summary>
        public int PageSize { get; set; } = 100;

        /// <summary>
        /// Maximum number of events to buffer in memory before pausing the fetching.
        /// The default is 1000
        /// </summary>
        public int MaximumStagedEventCount { get; set; } = 1000;

        /// <summary>
        /// Lower threshold of staged events in memory for a projection before
        /// the Async Daemon resumes fetching. The default is
        /// 500
        /// </summary>
        public int CooldownStagedEventCount { get; set; } = 500;
    }
}