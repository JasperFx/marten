namespace Marten.Events.Projections
{
    internal interface IProjectionSource
    {
        string ProjectionName { get; }

        IInlineProjection BuildInline(StoreOptions options);
        // TODO -- add an async option later.


    }
}
