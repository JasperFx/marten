using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Storage;

namespace Marten.Events.V4Concept
{
    public interface IStreamFragment
    {
        IList<IEvent> Events { get; }
    }

    public class StreamFragment<TDoc, TId>: IStreamFragment
    {
        public StreamFragment(TId id, ITenant tenant, IEnumerable<IEvent> events = null)
        {
            Id = id;
            Tenant = tenant;
            if (events != null)
            {
                Events.AddRange(events);
            }
        }

        public IList<IEvent> Events { get; } = new List<IEvent>();

        public TId Id { get;  }
        public ITenant Tenant { get; }

        public TDoc Aggregate { get; set; }
    }
}
