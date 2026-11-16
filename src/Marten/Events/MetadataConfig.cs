using Marten.Events.Schema;

namespace Marten.Events;

public interface IReadonlyMetadataConfig
{
    /// <summary>
    ///     Optional metadata describing the correlation id for an events
    /// </summary>
    public bool CorrelationIdEnabled { get; }

    /// <summary>
    ///     Optional metadata describing the causation id for an events
    /// </summary>
    public bool CausationIdEnabled { get; }

    /// <summary>
    ///     Optional, user defined headers for an event
    /// </summary>
    public bool HeadersEnabled { get; }

    /// <summary>
    ///     Setting to enable "last modified by" or user name tracking on individual events
    /// </summary>
    bool UserNameEnabled { get; set; }
}

public class MetadataConfig: IReadonlyMetadataConfig
{
    private readonly EventMetadataCollection _parent;

    internal MetadataConfig(EventMetadataCollection parent)
    {
        _parent = parent;
    }

    /// <summary>
    ///     Setting to enable optional correlation id metadata for events
    /// </summary>
    public bool CorrelationIdEnabled
    {
        get => _parent.CorrelationId.Enabled;
        set => _parent.CorrelationId.Enabled = value;
    }

    /// <summary>
    ///     Setting to enable optional causation id metadata for events
    /// </summary>
    public bool CausationIdEnabled
    {
        get => _parent.CausationId.Enabled;
        set => _parent.CausationId.Enabled = value;
    }

    /// <summary>
    ///     Setting to enable optional user defined metadata for events
    /// </summary>
    public bool HeadersEnabled
    {
        get => _parent.Headers.Enabled;
        set => _parent.Headers.Enabled = value;
    }

    /// <summary>
    ///     Setting to enable "last modified by" or user name tracking on individual events
    /// </summary>
    public bool UserNameEnabled
    {
        get => _parent.UserName.Enabled;
        set => _parent.UserName.Enabled = value;
    }

    /// <summary>
    ///     Enables the causation id, correlation id and headers metadata fields. In Marten 9.x this does
    ///     <b>not</b> enable the user name: set <see cref="UserNameEnabled" /> to <c>true</c> as well to store the
    ///     session's <c>LastModifiedBy</c> on events.
    /// </summary>
    /// <remarks>
    ///     Marten 10.0 also enables the user name here (marten#6012). That change is not backported to 9.x,
    ///     because in 9.x the <c>mt_events.user_name</c> column only exists while <see cref="UserNameEnabled" /> is
    ///     on, so enabling it would be a schema change.
    /// </remarks>
    public void EnableAll()
    {
        _parent.CausationId.Enabled = true;
        _parent.CorrelationId.Enabled = true;
        _parent.Headers.Enabled = true;
    }
}
