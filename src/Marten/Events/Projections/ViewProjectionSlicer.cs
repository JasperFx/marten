// using System.Collections.Generic;
// using System.Linq;
// using Marten.Events.Aggregation;
// using Marten.Storage;
//
// namespace Marten.Events.Projections
// {
//     public class ViewProjectionSlicer<TDoc, TId> : IEventSlicer<TDoc, TId>
//     {
//         public IReadOnlyList<EventSlice<TDoc, TId>> Slice(IEnumerable<StreamAction> streams, ITenancy tenancy)
//         {
//             return DoSlice(streams, tenancy).ToList();
//         }
//
//         public IReadOnlyList<TenantSliceGroup<TDoc, TId>> Slice(IReadOnlyList<IEvent> events, ITenancy tenancy)
//         {
//             var tenantGroups = events.GroupBy(x => x.TenantId);
//             return tenantGroups.Select(x => Slice(tenancy[x.Key], x.ToList())).ToList();
//         }
//
//         internal IEnumerable<EventSlice<TDoc, TId>> DoSlice(IEnumerable<StreamAction> streams, ITenancy tenancy)
//         {
//             var events = streams.SelectMany(x => x.Events);
//             var tenantGroups = events.GroupBy(x => x.TenantId);
//             foreach (var @group in tenantGroups)
//             {
//                 var tenant = tenancy[@group.Key];
//                 foreach (var slice in Slice(tenant, @group.ToArray()).Slices)
//                 {
//                     yield return slice;
//                 }
//             }
//         }
//
//         internal TenantSliceGroup<TDoc, TId> Slice(ITenant tenant, IList<IEvent> events)
//         {
//             var grouping = new EventGrouping<TId>();
//             foreach (var grouper in _groupers)
//             {
//                 grouper.Group(events, grouping);
//             }
//
//             return grouping.BuildSlices<TDoc>(tenant, _fanouts);
//         }
//     }
// }
