using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections;

public class SingleStreamProjectionTests
{
    [Fact]
    public void set_mapping_to_UseVersionFromMatchingStream_when_quick_append()
    {
        var projection = new SingleStreamProjection<User>();
        var mapping = DocumentMapping.For<User>();

        mapping.StoreOptions.EventGraph.AppendMode = EventAppendMode.Quick;

        projection.ConfigureAggregateMapping(mapping, mapping.StoreOptions);

        mapping.UseVersionFromMatchingStream.ShouldBeTrue();
    }

    [Fact]
    public void do_not_set_mapping_to_UseVersionFromMatchingStream_when_rich_append()
    {
        var projection = new SingleStreamProjection<User>();
        var mapping = DocumentMapping.For<User>();

        mapping.StoreOptions.EventGraph.AppendMode = EventAppendMode.Rich;

        projection.ConfigureAggregateMapping(mapping, mapping.StoreOptions);

        mapping.UseVersionFromMatchingStream.ShouldBeFalse();
    }
}
