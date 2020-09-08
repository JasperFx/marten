namespace Marten.Linq.Filters
{
    /// <summary>
    /// Marker interface to help Marten track whether or not a Linq
    /// query has some kind of tenant-aware filtering
    /// </summary>
    internal interface ITenantWhereFragment
    {
    }
}
