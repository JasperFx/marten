using System.Collections.Generic;
using System.Linq;
using Marten.Events.Aggregation;
using Marten.Storage;

namespace Marten.Events.Projections
{
    public class EventGrouping<TId>
    {
        private readonly Dictionary<TId, List<IEvent>> _events = new Dictionary<TId, List<IEvent>>();

        public void AddRange(TId id, IEnumerable<IEvent> events)
        {
            if (!_events.TryGetValue(id, out var list))
            {
                list = new List<IEvent>();
                _events.Add(id, list);
            }

            list.AddRange(events);
        }

        public TenantSliceGroup<T, TId> BuildSlices<T>(ITenant tenant, IReadOnlyList<IFanOutRule> rules)
        {
            var slices = buildSlices<T>(tenant);

            var group = new TenantSliceGroup<T, TId>(tenant, slices);

            if (rules.Any())
            {
                group.ApplyFanOutRules(rules);
            }

            return group;
        }

        private IEnumerable<EventSlice<T, TId>> buildSlices<T>(ITenant tenant)
        {
            return _events.Select(pair =>
            {
                return new EventSlice<T, TId>(pair.Key, tenant, pair.Value.Distinct().OrderBy(x => x.Sequence));
            });
        }
    }
}
