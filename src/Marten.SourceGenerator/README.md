# Marten.SourceGenerator (PoC initialized — tracking issue [#4405](https://github.com/JasperFx/marten/issues/4405))

Source generator package for Marten. Eliminates runtime code generation (no `JasperFx.RuntimeCompiler`, no `Roslyn` at runtime, no `FastExpressionCompiler`) for the constructs it covers. AOT-publishing-ready.

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
* **Then (iteration 4):** correctness + perf gates on three representative shapes (simple `Where`, `Where + OrderBy + Take`, `Where + Include<T>`) inside a **new isolated test harness** (`CompiledQueryTests`) running both the runtime-codegen and source-gen paths side-by-side. The scattered compiled-query tests currently in `LinqTests/Compiled`, `LinqTests/Bugs`, `LinqTests/Includes`, `LinqTests/ChildCollections`, `LinqTests/Acceptance`, `DocumentDbTests`, and `CoreTests` stay on the codegen path until the PoC lands green.
* **Final:** decision — continue Direction D for the rest of Marten, or fall back to packaging extraction for compiled queries specifically. If green: the scattered compiled-query tests migrate into `CompiledQueryTests`, the codegen-fallback bridge in `QueryCompiler.BuildQueryPlan` is removed (registry miss → throw, final shipping behavior), and the runtime codegen artifacts under each test project's `bin/.../Internal/Generated/CompiledQueries/` go away.

## Final shipping behavior (post-PoC, V9)

Compiled queries require the consumer to:

1. Add `<PackageReference Include="Marten.SourceGenerator" PrivateAssets="all" />` to the project defining the `ICompiledQuery<TDoc, TOut>` types.
2. Mark the assembly with `[assembly: JasperFxAssembly]`.

Without both, `Session.Query(myCompiledQuery)` throws `InvalidOperationException`. There is no runtime codegen fallback. Direction D treats this as an acceptable cost of eliminating `JasperFx.RuntimeCompiler` from the compiled-query path; the alternative (packaging compiled queries into a separate extension NuGet) remains available if the PoC fails.

See [#4405](https://github.com/JasperFx/marten/issues/4405) for the full success / failure criteria and timeline.

## Discovery model

The generator is **gated by `[JasperFxAssembly]`** on the consumer assembly — without the marker, the generator emits nothing. Within a marked assembly, the generator finds every type implementing `ICompiledQuery<TDoc, TOut>` and emits scaffolding for it.

Discovery for other constructs (documents, ancillary stores) follows the same gating pattern with construct-specific attributes — see [#4404](https://github.com/JasperFx/marten/issues/4404) for the full attribute matrix.

## Packaging

Analyzer-only NuGet — no runtime payload. Consumers reference the package the same way they reference `Marten.Newtonsoft` (additive opt-in). The package's `IncludeBuildOutput=false` + `analyzers/dotnet/cs` packing path ensures the source generator runs at consumer compile time without bringing a runtime dependency.
