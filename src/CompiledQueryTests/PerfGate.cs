using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Internal.CompiledQueries;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace CompiledQueryTests;

/// <summary>
/// Perf gate for #4405 iteration 4. Compares the source-gen path against the
/// codegen-bridge path on the same compiled query type using two separate
/// <see cref="DocumentStore"/> instances. Each store has an independent
/// <c>CompiledQueryCollection._querySources</c> cache, so we can prime one
/// store with the source-gen path (descriptor in registry) and the other
/// with the codegen path (descriptor temporarily removed) without
/// cross-contamination.
/// </summary>
/// <remarks>
/// <para>
/// The numbers reported by this gate are not benchmark-grade — they're a
/// sanity check that source-gen is <i>at least</i> in the same league as
/// codegen at steady state, and clearly faster on the cold first call (where
/// the codegen path runs Roslyn emit). For a publishable benchmark we'd use
/// BenchmarkDotNet; for the PoC decision we just need the order-of-magnitude.
/// </para>
/// <para>
/// Steady-state numbers are dominated by Postgres round-trip latency for
/// queries that actually hit the DB. To isolate the CPU-side dispatch cost
/// we'd need a no-op connection adapter; for the PoC, comparing total
/// wall-clock for N invocations on both paths against the same Postgres
/// instance gives us the relative cost as a fraction of total query time.
/// </para>
/// </remarks>
public class PerfGate: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public PerfGate(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task source_gen_cold_call_beats_codegen_cold_call_on_shape_1()
    {
        // Both stores use the same schema + same seed data so the only
        // difference between runs is whether the descriptor is registered.
        var seed = new[]
        {
            new User { FirstName = "Cold", LastName = "Run", UserName = "cold_a" },
            new User { FirstName = "Cold", LastName = "Run", UserName = "cold_b" },
        };

        // Capture the descriptor before bypassing — re-register at end so other
        // tests in the same xunit run aren't disturbed.
        var saved = CompiledQueryHandlerRegistry.Unregister(typeof(UserByUserNameShape));
        saved.ShouldNotBeNull("Expected the source generator's ModuleInitializer to have registered UserByUserNameShape before any test runs.");

        long codegenColdMs;
        try
        {
            using var codegenStore = SeparateStore();
            await codegenStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await codegenStore.BulkInsertDocumentsAsync(seed);

            await using var codegenSession = codegenStore.QuerySession();
            var sw = Stopwatch.StartNew();
            var result = await codegenSession.QueryAsync(new UserByUserNameShape { UserName = "cold_b" });
            sw.Stop();
            codegenColdMs = sw.ElapsedMilliseconds;
            result.ShouldNotBeNull();
            result!.UserName.ShouldBe("cold_b");
        }
        finally
        {
            // Re-register so the source-gen run + any later tests see a populated registry.
            CompiledQueryHandlerRegistry.Register(typeof(UserByUserNameShape), saved!);
        }

        long sourceGenColdMs;
        {
            using var sourceGenStore = SeparateStore();
            await sourceGenStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await sourceGenStore.BulkInsertDocumentsAsync(seed);

            await using var sourceGenSession = sourceGenStore.QuerySession();
            var sw = Stopwatch.StartNew();
            var result = await sourceGenSession.QueryAsync(new UserByUserNameShape { UserName = "cold_b" });
            sw.Stop();
            sourceGenColdMs = sw.ElapsedMilliseconds;
            result.ShouldNotBeNull();
            result!.UserName.ShouldBe("cold_b");
        }

        _output.WriteLine($"Cold call — codegen: {codegenColdMs}ms; source-gen: {sourceGenColdMs}ms");

        // Source-gen should be at minimum no slower than codegen on cold call.
        // In practice it's expected to be dramatically faster because it skips
        // CompiledQuerySourceBuilder.AssembleTypes (Roslyn emit + Activator.CreateInstance).
        // We assert a generous margin to avoid flakes — sub-50ms slack is more than
        // enough headroom for both paths on a warm host.
        sourceGenColdMs.ShouldBeLessThanOrEqualTo(codegenColdMs + 50,
            $"Source-gen cold call ({sourceGenColdMs}ms) regressed beyond noise margin vs codegen ({codegenColdMs}ms).");
    }

    [Fact]
    public async Task source_gen_steady_state_is_within_noise_margin_of_codegen_on_shape_1()
    {
        const int InvocationCount = 200;
        var seed = new[]
        {
            new User { FirstName = "Hot", LastName = "Run", UserName = "hot_a" },
            new User { FirstName = "Hot", LastName = "Run", UserName = "hot_b" },
        };

        var saved = CompiledQueryHandlerRegistry.Unregister(typeof(UserByUserNameShape));
        saved.ShouldNotBeNull();

        long codegenSteadyMs;
        try
        {
            using var codegenStore = SeparateStore();
            await codegenStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await codegenStore.BulkInsertDocumentsAsync(seed);

            await using var session = codegenStore.QuerySession();
            // Prime — codegen emits its Roslyn assembly on first call.
            _ = await session.QueryAsync(new UserByUserNameShape { UserName = "hot_a" });

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < InvocationCount; i++)
            {
                _ = await session.QueryAsync(new UserByUserNameShape { UserName = "hot_b" });
            }
            sw.Stop();
            codegenSteadyMs = sw.ElapsedMilliseconds;
        }
        finally
        {
            CompiledQueryHandlerRegistry.Register(typeof(UserByUserNameShape), saved!);
        }

        long sourceGenSteadyMs;
        {
            using var sourceGenStore = SeparateStore();
            await sourceGenStore.Advanced.Clean.CompletelyRemoveAllAsync();
            await sourceGenStore.BulkInsertDocumentsAsync(seed);

            await using var session = sourceGenStore.QuerySession();
            // Prime — source-gen registers the per-store source on first call.
            _ = await session.QueryAsync(new UserByUserNameShape { UserName = "hot_a" });

            var sw = Stopwatch.StartNew();
            for (var i = 0; i < InvocationCount; i++)
            {
                _ = await session.QueryAsync(new UserByUserNameShape { UserName = "hot_b" });
            }
            sw.Stop();
            sourceGenSteadyMs = sw.ElapsedMilliseconds;
        }

        var codegenPerCallUs = codegenSteadyMs * 1000.0 / InvocationCount;
        var sourceGenPerCallUs = sourceGenSteadyMs * 1000.0 / InvocationCount;
        _output.WriteLine($"Steady state — codegen: {codegenSteadyMs}ms over {InvocationCount} calls ({codegenPerCallUs:F1}us/call); source-gen: {sourceGenSteadyMs}ms ({sourceGenPerCallUs:F1}us/call)");

        // Steady-state is dominated by Postgres round-trip; CPU-side dispatch
        // differences are usually <1% of total. Allow generous slack — the gate
        // is "no order-of-magnitude regression", not "exact equality".
        sourceGenSteadyMs.ShouldBeLessThan((long)(codegenSteadyMs * 1.5 + 100),
            $"Source-gen steady state ({sourceGenSteadyMs}ms / {InvocationCount} calls) regressed beyond 1.5x vs codegen ({codegenSteadyMs}ms).");
    }
}
