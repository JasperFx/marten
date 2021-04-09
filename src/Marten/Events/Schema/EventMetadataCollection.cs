using System.Collections.Generic;
using Marten.Storage;
using Marten.Storage.Metadata;

namespace Marten.Events.Schema
{
    public class EventMetadataCollection
    {
        public MetadataColumn CausationId { get; } = new CausationIdColumn { Enabled = false};
        public MetadataColumn CorrelationId { get; } = new CorrelationIdColumn{ Enabled = false};
        public MetadataColumn Headers { get; } = new HeadersColumn{ Enabled = false};
    }
}
