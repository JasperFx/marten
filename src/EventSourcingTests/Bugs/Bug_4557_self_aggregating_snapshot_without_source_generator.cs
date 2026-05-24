using System;
using System.Threading.Tasks;
using JasperFx.Events.Projections;
using Marten;
using Marten.Testing.Harness;
using Marten.Testing.OtherAssembly.Issue4557;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Bugs;

// Reproduction for https://github.com/JasperFx/marten/issues/4557.
//
// A user upgrading 8 -> 9 has a self-aggregating immutable `record` snapshot with
// static Create/Apply, registered via `Projections.Snapshot<MyType>(Inline)`. It
// worked in v8 (runtime codegen) but in v9 fails at store construction with:
//
//   JasperFx.Events.Projections.InvalidProjectionException:
//   No source-generated dispatcher found for
//   Marten.Events.Aggregation.SingleStreamProjection<MyType, System.Guid>...
//
// The aggregate type + the Snapshot<MyType> registration both live in
// Marten.Testing.OtherAssembly, which references Marten but NOT the
// JasperFx.Events.SourceGenerator analyzer — faithfully mirroring a real consumer
// app whose csproj only has `<PackageReference Include="Marten" />`. Marten hides
// the analyzer with PrivateAssets=all, so the generator never runs in the consumer
// assembly, no `[GeneratedEvolver]` attribute is emitted, and the runtime scan in
// JasperFxAggregationProjectionBase.tryUseAssemblyRegisteredEvolver finds nothing.
//
// (Defining these same types directly in EventSourcingTests would NOT reproduce,
// because this test assembly references the analyzer explicitly and Pipeline 3 of
// the generator would emit the dispatcher.)
public class Bug_4557_self_aggregating_snapshot_without_source_generator
{
    // Current (broken) behavior — pins the exact failure so the branch stays green
    // while we discuss the fix. The throw happens at DocumentStore.For(...) config
    // time, before any database access.
    [Fact]
    public void snapshot_without_source_generator_throws_at_registration()
    {
        var ex = Should.Throw<JasperFx.Events.Projections.InvalidProjectionException>(() =>
        {
            using var store = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                Issue4557Registration.Configure(opts);
            });
        });

        ex.Message.ShouldContain("No source-generated dispatcher found");
    }

    // Desired behavior once the root cause is addressed: a convention-based
    // self-aggregating Snapshot<T> in an assembly without the source generator
    // should still work (the migration guide promises "Marten falls back to runtime
    // evolver lookup for those"). Skipped until that runtime fallback lands so CI
    // stays green; un-skip when the fix is in place.
    [Fact(Skip = "Reproduces #4557 — un-skip once the JasperFx runtime evolver fallback for convention methods lands.")]
    public async Task snapshot_without_source_generator_should_round_trip()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "issue4557";
            Issue4557Registration.Configure(opts);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();

        var streamId = Guid.NewGuid();
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream<MyType>(streamId,
                new MyTypeCreated(streamId, "test"),
                new MyTypeIncremented(1));
            await session.SaveChangesAsync();
        }

        await using (var query = store.QuerySession())
        {
            var snapshot = await query.LoadAsync<MyType>(streamId);
            snapshot.ShouldNotBeNull();
            snapshot.Name.ShouldBe("test");
            snapshot.Count.ShouldBe(1);
            snapshot.LastEvent.ShouldBe(nameof(MyTypeIncremented));
        }
    }
}
