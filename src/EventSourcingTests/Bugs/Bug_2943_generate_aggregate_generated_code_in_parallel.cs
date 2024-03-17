using System;
using System.Linq;
using System.Threading.Tasks;
using EventSourcingTests.Projections.CodeGeneration;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Internal.CodeGeneration;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_2943_generate_aggregate_generated_code_in_parallel
{
    [Fact]
    public void aggregates_do_not_fail_code_generation_on_parallel_execution()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        // Given
        options.Projections.LiveStreamAggregation<ProjectionCodeGenerationTests.Something>();

        // When
        var store = new DocumentStore(options);
        Parallel.For(1, 100, _ =>
        {
            Parallel.ForEach(store.Events.As<ICodeFileCollection>().BuildFiles().OfType<IProjectionSource>(), projection =>
            {
                projection.Build(store);
            });
        });

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<ProjectionCodeGenerationTests.Something>>()
            .ShouldHaveSingleItem();

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(ProjectionCodeGenerationTests.Something).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void aggregates_do_not_fail_code_generation_on_parallel_FetchForWriting_execution()
    {
        var options = new StoreOptions();
        options.Connection(ConnectionSource.ConnectionString);

        // Given
        options.Projections.LiveStreamAggregation<ProjectionCodeGenerationTests.Something>();

        // When
        var store = new DocumentStore(options);
        Parallel.For(1, 100, _ =>
        {
            store.LightweightSession().Events.FetchForWriting<ProjectionCodeGenerationTests.Something>(Guid.NewGuid()).GetAwaiter().GetResult();
        });

        // Then
        store.Events.As<ICodeFileCollection>().BuildFiles()
            .OfType<SingleStreamProjection<ProjectionCodeGenerationTests.Something>>()
            .ShouldHaveSingleItem();

        options.BuildFiles()
            .OfType<DocumentProviderBuilder>()
            .Where(e => e.ProviderName == typeof(ProjectionCodeGenerationTests.Something).ToSuffixedTypeName("Provider"))
            .ShouldHaveSingleItem();
    }
}
