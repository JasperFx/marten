using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Storage;

namespace Marten.Events.V4Concept.Aggregation
{
    public static class StreamFragmentSplitter
    {
        public static IReadOnlyList<StreamFragment<T, Guid>> SplitByStreamId<T>(IEnumerable<IEvent> events,
            ITenancy storeTenancy)
        {
            return events.GroupBy(x => x.StreamId).Select(group =>
                    new StreamFragment<T, Guid>(@group.Key, storeTenancy.Default, @group))
                .ToList();
        }

        public static IReadOnlyList<StreamFragment<T, string>> SplitByStreamKey<T>(IEnumerable<IEvent> events,
            ITenancy storeTenancy)
        {
            return events.GroupBy(x => x.StreamKey).Select(group =>
                    new StreamFragment<T, string>(@group.Key, storeTenancy.Default, @group))
                .ToList();
        }

        internal static IEnumerable<StreamFragment<TDoc, TId>> SplitByTenant<TDoc, TId>(
            this IGrouping<TId, IEvent> grouping, ITenancy storeTenancy)
        {
            return grouping.GroupBy(x => x.TenantId)
                .Select(x => new StreamFragment<TDoc, TId>(grouping.Key, storeTenancy[x.Key], x));
        }


        public static IReadOnlyList<StreamFragment<T, Guid>> SplitByStreamIdMultiTenanted<T>(IEnumerable<IEvent> events,
            ITenancy storeTenancy)
        {
            return events
                .GroupBy(x => x.StreamId)
                .SelectMany(g => g.SplitByTenant<T, Guid>(storeTenancy))
                .ToList();
        }

        public static IReadOnlyList<StreamFragment<T, string>> SplitByStreamKeyMultiTenanted<T>(IEnumerable<IEvent> events,
            ITenancy storeTenancy)
        {
            return events
                .GroupBy(x => x.StreamKey)
                .SelectMany(g => g.SplitByTenant<T, string>(storeTenancy))
                .ToList();
        }

    }
}
