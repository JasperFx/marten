namespace Marten.Events.V4Concept
{
    internal class InlineProjectionSource: IProjectionSource
    {
        private readonly IInlineProjection _projection;

        public InlineProjectionSource(IInlineProjection projection)
        {
            _projection = projection;

            // TODO -- this probably gets fancier later
            ProjectionName = projection.GetType().FullName;
        }

        public string ProjectionName { get; }
        public IInlineProjection BuildInline(StoreOptions options)
        {
            return _projection;
        }
    }
}