namespace Marten.Events.Projections.Async.ErrorHandling
{
    public enum ExceptionAction
    {
        Retry,
        Pause,
        Nothing,
        StopAll,
        Stop
    }
}