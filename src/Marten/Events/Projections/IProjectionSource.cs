namespace Marten.Events.Projections
{
    internal interface IProjectionSource
    {
        string ProjectionName { get; }

        IProjection Build(DocumentStore store);
    }
}
