using System;

namespace Marten.Events
{
    [Obsolete("No longer used. Will be removed in version 4.")]
    public enum ProjectionType
    {
        transform,
        aggregate,
        stream_aggregate,
        snapshot,
        by_category
    }
}
