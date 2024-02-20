using System;
using EventSourcingTests.Aggregation;
using JasperFx.CodeGeneration;
using JasperFx.RuntimeCompiler;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_2607_aggregate_projection_with_guid_argument
{
    private readonly ITestOutputHelper _output;

    public Bug_2607_aggregate_projection_with_guid_argument(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void codegen_will_ignore_the_constructor_with_args_that_cannot_be_an_event()
    {
        var projection = new SingleStreamProjection<BadCtorAggregate>();
        projection.Compile(new StoreOptions());

        projection.InitializeSynchronously(new GenerationRules(), new EventGraph(new StoreOptions()), null);

        var code = projection.SourceCode();
        code.ShouldNotContain("Marten.Events.IEvent<System.Guid>");
    }
}

public class BadCtorAggregate
{
    public Guid Id { get; set; }

    public BadCtorAggregate()
    {
    }

    public BadCtorAggregate(Guid id)
    {
    }

    public int Count { get; set; }

    public void Apply(AEvent e) => Count++;
}
