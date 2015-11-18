namespace Marten.Events
{
    public enum ProjectionType
    {
        transform,
        aggregate,
        stream_aggregate,
        snapshot,
        by_category
    }
}