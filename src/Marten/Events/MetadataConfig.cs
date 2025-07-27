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
    ///     Method to enable all optional metadata fields
    /// </summary>
    public void EnableAll()
    {
        _parent.CausationId.Enabled = true;
        _parent.CorrelationId.Enabled = true;
        _parent.Headers.Enabled = true;
    }
}
