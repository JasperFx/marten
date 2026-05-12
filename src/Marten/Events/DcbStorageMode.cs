namespace Marten.Events;

/// <summary>
/// Controls how Dynamic Consistency Boundary (DCB) tags are stored on the event store.
/// </summary>
public enum DcbStorageMode
{
    /// <summary>
    /// One Postgres table per registered tag type (<c>mt_event_tag_&lt;suffix&gt;</c>).
    /// DCB queries LEFT JOIN across these tables. This is the default and the behavior
    /// shipped in Marten 8.
    /// </summary>
    TagTables,

    /// <summary>
    /// A single <c>hstore</c> column on <c>mt_events</c> stores all tags inline as
    /// key-value pairs (key = tag type suffix, value = stringified tag value). DCB
    /// queries use Postgres' <c>@&gt;</c> containment operator against a single GIN
    /// index covering all tag types — no JOINs.
    ///
    /// Trade-offs:
    /// <list type="bullet">
    /// <item><description>All tag values are stored as text (string conversion happens automatically).</description></item>
    /// <item><description>The Postgres <c>hstore</c> extension is automatically registered as part of schema creation.</description></item>
    /// <item><description>Not interchangeable with <see cref="TagTables"/> for existing stores — pick one mode per database from creation.</description></item>
    /// </list>
    /// </summary>
    HStore
}
