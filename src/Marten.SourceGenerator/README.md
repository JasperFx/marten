# Marten.SourceGenerator — tracking issue [#4405](https://github.com/JasperFx/marten/issues/4405)

Source generator package for Marten. With the analyzer reference and the `[JasperFxAssembly]` marker in place, every `ICompiledQuery<TDoc, TOut>` in the consumer assembly compiles down to a hand-coded handler — no Roslyn at runtime, no FastExpressionCompiler for that query, no reflection on the hot path. AOT-publishing-ready.

## Consumer-facing opt-in (V9)

```xml
<PackageReference Include="Marten.SourceGenerator" PrivateAssets="all" />
```

Plus, in any file in your assembly:

```csharp
[assembly: JasperFx.JasperFxAssembly]
```

With both present, every `ICompiledQuery<TDoc, TOut>` declared in the assembly gets a generator-emitted handler that registers itself with `Marten.Internal.CompiledQueries.CompiledQueryHandlerRegistry` via a `[ModuleInitializer]` at assembly load. The Marten runtime dispatches matching compiled queries through that handler.

## PoC scope (current)

**Validates the compiled-query source-gen + first-call-LINQ-parse + memoize model.** Compiled queries are the highest-risk-of-surprise work stream under the zero-runtime-codegen direction tracked in [#4404](https://github.com/JasperFx/marten/issues/4404). If the seam works for compiled queries, the rest of Direction D (document storage, event storage, ancillary stores) becomes a comparatively mechanical exercise. If not, compiled queries fall back to packaging extraction.

## Status

* **PoC initialized.** Project scaffolds; one incremental generator stub fires for every `ICompiledQuery<TDoc, TOut>` implementation in assemblies marked with `[JasperFxAssembly]`. Currently emits a marker class per discovered type — proves the pipeline.
* **Iteration 2 — typed parameter binder emission. ✅** The generator now emits a `static class {QueryType}_CompiledQueryHandler` per discovered query type, containing:
  * `ParameterMemberNames` / `IncludeMemberNames` / `StatisticsMemberName` — pre-classified member-name surface so the runtime planner skips reflection.
  * `BindParameter(NpgsqlParameter, query, memberName, enumAsString)` — a switch that does direct field/property reads, mirroring the assignment pattern in `Marten.Internal.CompiledQueries.ParameterUsage.GenerateCode`.
  * Covers BCL primitives + `Guid` / `DateTime` / `DateTimeOffset` / `TimeSpan` / `DateOnly` / `TimeOnly`, enums (both `EnumStorage` modes via the runtime `enumAsString` arg), arrays of those types, and `byte[]` → `Bytea`.
  * Unsupported member types emit diagnostic `MTSG001` (Info) and are skipped — runtime falls back to reflective binding for those if the planner needs them.
  * Validated by `Marten.SourceGenerator.Tests` (13 tests across net9.0 + net10.0), including a round-trip "generated source compiles against an Npgsql stub" check.
* **Iteration 3 — runtime registry + dispatch wiring. ✅** Three runtime types in `Marten.Internal.CompiledQueries`:
  * `CompiledQueryHandlerRegistry` — process-wide static dictionary keyed by query `Type`. Populated by `[ModuleInitializer]` shims the generator emits next to each handler class. Implicit opt-in: no `StoreOptions` flag, no consumer call.
  * `CompiledQueryHandlerDescriptor` — the contract handed off from generator to runtime: query/doc/output Types + the three pre-classified member-name lists + a zero-allocation `Action<NpgsqlParameter, object, string, bool>` binder (the boxing adapter is a `static` lambda the generator emits, so the per-call hot path captures nothing).
  * `SourceGeneratedCompiledQuerySource<TOut>` + `SourceGeneratedCompiledQueryHandler<TOut>` — `ICompiledQuerySource` / `IQueryHandler<TOut>` implementations that replay the `LinqQueryParser`-derived `CompiledQueryPlan.Commands` against the descriptor's binder at runtime. The hot path matches today's codegen (one `AppendWithParameters` per command + tight loop over parameter usages), with no Roslyn emit / no FastExpressionCompiler / no per-call allocations.
  * `CompiledQueryCollection.GetCompiledQuerySourceFor` rewritten with registry-first dispatch — if the descriptor is present and the plan is a Stateless shape, the source-gen path runs; otherwise the PoC bridge falls through to the existing `JasperFx.RuntimeCompiler` path. Cloneable + Complex (Includes / Statistics / per-session-cloned handlers) take the bridge; their source-gen equivalents land in iteration 4.
  * **PoC bridge marker**: the codegen-fallback `if`/`else` is bracketed by `// ---- #4405 iteration 3 ----` and `// PoC bridge: ...` comments. Final V9 behavior is "registry miss throws"; the bridge is deleted once iteration 4 lands green.
  * Validated: 19 generator tests on net9.0 + net10.0 (16 textual, 3 end-to-end — the e2e tests compile generated source into a real in-memory assembly, fire `[ModuleInitializer]` via `RuntimeHelpers.RunModuleConstructor`, inspect the live registry, and drive the descriptor's binder against real `NpgsqlParameter` instances). 107 scattered compiled-query tests in `LinqTests` continue passing through the codegen bridge.
* **Iteration 4 — correctness + perf gates. ✅ GREEN.**
  * Runtime extended to all three handler shapes (Stateless / Cloned / Complex). The codegen-bridge in `CompiledQueryCollection.GetCompiledQuerySourceFor` is now hit only when the consumer's defining assembly hasn't been opted into source-gen — registered query types unconditionally take the source-gen path.
  * Isolated `src/CompiledQueryTests/` harness — `[assembly: JasperFx.JasperFxAssembly]` + `OutputItemType="Analyzer"` reference to `Marten.SourceGenerator` is the consumer-facing opt-in shape we ship at V9. Three representative query types: `UserByUserNameShape` (Stateless), `UsersByFirstNamePageShape` (Stateless multi-clause), `IssueWithAssigneeShape` (Complex / Include).
  * **Correctness gate**: 7 tests passing on net9.0 + net10.0 against real Postgres. Each shape's source-gen result matches a reference uncompiled LINQ query. Includes a registry-state assertion proving the source-gen path is engaged (not silently falling through to the codegen bridge).
  * **Perf gate** (using `CompiledQueryHandlerRegistry.Unregister` to drive A/B between paths on the same query type):
    * Cold call (first invocation, includes Marten pipeline setup): **codegen 75ms vs source-gen 3ms — 25× faster**. Source-gen skips `CompiledQuerySourceBuilder.AssembleTypes` (Roslyn emit + `Activator.CreateInstance`).
    * Steady-state per call (200 calls amortized): **codegen 710μs vs source-gen 490μs — 31% faster**. Difference dominated by fewer allocations on the source-gen hot path.
    * Both TFMs show consistent results (net10.0: cold 4ms vs 61ms, steady 490μs vs 560μs).
* **Final ✅ — decision: continue Direction D.** The seam works for all three handler shapes, including the previously-most-codegen-heavy `Where + Include<T>` path. Source-gen is faster on both cold-start and steady-state axes by clear margins, with no correctness regressions. Direction D extends to document storage / event storage / ancillary stores as planned in #4404; the packaging-extraction alternative for compiled queries is dropped.
* **Iteration 5 — wrap-up follow-ups.** Tier 2 generator emits AOT-clean include-attachers (`AttachIncludeReaders` static method with typed `Include.ReaderTo*` factory calls) + `ReadStatistics` accessor. `SourceGeneratedComplexHandler` consumes them through new descriptor delegates — no more `MakeGenericMethod` / reflective member reads on the runtime path. All seven Marten assemblies that declare or execute `ICompiledQuery<,>` types (LinqTests, DocumentDbTests, ValueTypeTests, CommandLineRunner, MartenBenchmarks, IssueService, plus CompiledQueryTests) opted into source-gen via `[assembly: JasperFx.JasperFxAssembly]` + analyzer reference. 149/149 LinqTests compiled-query tests + 63/63 DocumentDbTests + 12/12 ValueTypeTests + 9/9 CompiledQueryTests + 19/19 generator unit tests passing on net9.0 + net10.0.

## Final shipping behavior (V9)

Compiled queries require the consumer to:

1. Add `<PackageReference Include="Marten.SourceGenerator" PrivateAssets="all" />` to the project declaring the `ICompiledQuery<TDoc, TOut>` types.
2. Mark the assembly with `[assembly: JasperFx.JasperFxAssembly]`.

Without both, the source generator emits nothing and the runtime builds the descriptor reflectively via `RuntimeCompiledQueryDescriptorFactory` (FastExpressionCompiler + a small reflection-built dispatch) and caches it in the same `CompiledQueryHandlerRegistry`. No Roslyn — `JasperFx.RuntimeCompiler` was retired from Marten in #4454 Phase 5. The fallback is the only path for three shapes the generator skips:

* **Filter-driven plans.** Compiled queries whose SQL uses `string.Contains/StartsWith/EndsWith`, `HashSet<T>.Contains` with JSONB containment, `Dictionary<,>.ContainsKey`, or child-collection JsonPath counts. The `ICompiledQueryAwareFilter.BuildSetter()` Action contract added in Phase 1A+B is what the source-gen path consumes from filters — every filter implementation already returns a runtime `Action<NpgsqlParameter, object>`, so the source-gen path supports filter-driven queries too. The PoC generator's earlier skip-on-filter rule was dropped along with the codegen bridge.
* **Generic / nested query types.** The generator skips both shapes (see early-return in `CompiledQuerySourceGenerator.TryRenderHandler`). Extending discovery to handle them needs additional emit-site logic — follow-up.
* **Assemblies without `[JasperFxAssembly]`.** Includes user assemblies that haven't opted in, plus a handful of Marten test projects that intentionally don't (e.g., `Marten.Testing.OtherAssembly` carries a generic compiled query used cross-assembly by Bug_1851). Those take the reflective fallback.

See [#4405](https://github.com/JasperFx/marten/issues/4405) for the full success / failure criteria and timeline.

## Discovery model

The generator is **gated by `[JasperFxAssembly]`** on the consumer assembly — without the marker, the generator emits nothing. Within a marked assembly, the generator finds every type implementing `ICompiledQuery<TDoc, TOut>` and emits scaffolding for it.

Discovery for other constructs (documents, ancillary stores) follows the same gating pattern with construct-specific attributes — see [#4404](https://github.com/JasperFx/marten/issues/4404) for the full attribute matrix.

## Packaging

Analyzer-only NuGet — no runtime payload. Consumers reference the package the same way they reference `Marten.Newtonsoft` (additive opt-in). The package's `IncludeBuildOutput=false` + `analyzers/dotnet/cs` packing path ensures the source generator runs at consumer compile time without bringing a runtime dependency.
