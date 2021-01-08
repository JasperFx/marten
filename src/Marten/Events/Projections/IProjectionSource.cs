namespace Marten.Events.Projections
{
    internal interface IProjectionSource
    {
        string ProjectionName { get; }

        IInlineProjection BuildInline(DocumentStore store);
        // TODO -- add an async option later.


    }
}
