using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections.Flattened;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_3643_using_struct_in_map_flattableprojection: BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3643_using_struct_in_map_flattableprojection(ITestOutputHelper output)
    {
        _output = output;
    }


    [Fact]
    public async Task add_event_that_requires_mapping_a_struct_and_a_class()
    {
        StoreOptions(x =>
        {
            x.Projections.Add<MyTableProjection>(ProjectionLifecycle.Inline);
            x.Logger(new TestOutputMartenLogger(_output));
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var e = new MyEvent(Guid.NewGuid(), new MyStruct("StructValue"), null);
        theSession.Events.StartStream(Guid.NewGuid(), e);
        await theSession.SaveChangesAsync();

        (string structvalue, string classvalue) result =
            (await theSession.AdvancedSql.QueryAsync<string, string>(
                "SELECT ROW(my_struct_primitive_value), ROW(my_class_primitive_value) FROM bugs.my_table_projection",
                CancellationToken.None)).First();
        result.structvalue.ShouldBe("StructValue");
        result.classvalue.ShouldBeNull();
    }

    public record MyEvent(Guid Id, MyStruct MyStruct, MyClass MyClass);

    public record struct MyStruct(string PrimitiveValue);

    public record MyClass(string PrimitiveValue);

    public class MyTableProjection: FlatTableProjection
    {
        public MyTableProjection(): base("my_table_projection", SchemaNameSource.EventSchema)
        {
            Table.AddColumn<Guid>("id").AsPrimaryKey();
            Options.TeardownDataOnRebuild = true;

            Project<MyEvent>(map =>
            {
                map.Map(x => x.MyStruct.PrimitiveValue);
                map.Map(x => x.MyClass.PrimitiveValue);
            });
        }
    }
}
