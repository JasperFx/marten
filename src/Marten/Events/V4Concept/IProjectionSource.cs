using Marten.Storage;

namespace Marten.Events.V4Concept
{
    internal interface IProjectionSource
    {
        string ProjectionName { get; }

        IInlineProjection BuildInline(StoreOptions options);
        // TODO -- add an async option later.


    }
}
