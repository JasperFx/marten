using Marten.Storage.Metadata;

namespace Marten.Events.Schema;

/// <summary>
///     The optional event metadata columns. All of them are written and read by default since 10.0 (#5812);
///     <c>StoreOptions.RestoreV9Defaults()</c> or <c>Events.MetadataConfig</c> turns them off.
/// </summary>
public class EventMetadataCollection
{
    public MetadataColumn CausationId { get; } = new CausationIdColumn { Enabled = true };
    public MetadataColumn CorrelationId { get; } = new CorrelationIdColumn { Enabled = true };
    public MetadataColumn Headers { get; } = new HeadersColumn { Enabled = true };
    public MetadataColumn UserName { get; } = new UserNameColumn() { Enabled = true };
}
