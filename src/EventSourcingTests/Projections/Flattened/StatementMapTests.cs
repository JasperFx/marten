using System;
using System.Reflection;
using Marten.Events.Projections.Flattened;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Projections.Flattened;

public class StatementMapTests
{
    [Fact]
    public void add_column_by_property()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.EventSchema);
        var map = new StatementMap<ValuesSet>(projection, Array.Empty<MemberInfo>());
        map.Map(x => x.A);

        var column = projection.Table.ColumnFor("a");
        column.Type.ShouldBe("integer");
    }
}
