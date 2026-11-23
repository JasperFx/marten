using Marten.Storage.Metadata;

namespace Marten.Events.Schema;

/// <summary>
///     The optional event metadata columns. Off by default, as in Marten 9: #6144 put the 10.0 alphas' default back
///     after the rc.1 perf round measured them as most of the remaining append cost. Turn them on through
///     <c>Events.MetadataConfig</c>. The columns are created whatever these say (#5309); they govern only what is
///     written and read.
/// </summary>
public class EventMetadataCollection
{
    public MetadataColumn CausationId { get; } = new CausationIdColumn { Enabled = false };
    public MetadataColumn CorrelationId { get; } = new CorrelationIdColumn { Enabled = false };
    public MetadataColumn Headers { get; } = new HeadersColumn { Enabled = false };
    public MetadataColumn UserName { get; } = new UserNameColumn() { Enabled = false };
}
