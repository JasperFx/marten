namespace Marten.Events.V4Concept
{
    internal interface ILiveAggregatorSource<T>
    {
        ILiveAggregator<T> Build(StoreOptions options);
    }
}
