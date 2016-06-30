namespace Marten.Events.Projections.Async
{
    public class AsyncOptions
    {
        public int PageSize { get; set; } = 100;
        public int MaximumStagedEventCount { get; set; } = 1000;
    }
}