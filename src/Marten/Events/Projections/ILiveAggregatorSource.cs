namespace Marten.Events.Projections
{
    internal interface ILiveAggregatorSource<T>
    {
        ILiveAggregator<T> Build(StoreOptions options);
    }
}
