#nullable enable
namespace Marten.Events.Dcb;

/// <summary>
/// #5887: the <c>mt_dcb_tag_version.tag_table</c> discriminator for a per-event-type version.
/// </summary>
/// <remarks>
/// A tag value's versions share one table: the tag type's table suffix keys the version every
/// event carrying the value moves, and <c>{suffix}:{event type name}</c> keys the one only events
/// of that type move. Tag table suffixes are SQL identifiers and never contain a colon, so the two
/// can never collide, and no schema change is needed for the second kind.
/// </remarks>
internal static class DcbTagVersionKeys
{
    public static string ForEventType(string tagTableSuffix, string eventTypeName)
        => tagTableSuffix + ":" + eventTypeName;
}
