using System.Collections.Generic;

#nullable enable
namespace Marten.Internal
{
    public class MetadataOverrides
    {
        public MetadataOverrides(object @event)
        {
            Event = @event;
        }

        public object Event { get; }

        /// <summary>
        /// Optional metadata describing the causation id for this
        /// unit of work
        /// </summary>
        public string? CausationId { get; set; }

        /// <summary>
        /// Optional metadata describing the correlation id for this
        /// unit of work
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// Optional metadata values. This may be null.
        /// </summary>
        public Dictionary<string, object>? Headers { get; set; }
    }
}
