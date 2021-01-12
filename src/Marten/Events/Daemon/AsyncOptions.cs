namespace Marten.Events.Daemon
{
    public class AsyncOptions
    {
        public int BatchSize { get; set; } = 500;
        public int MaximumHopperSize { get; set; } = 2500;
    }
}
