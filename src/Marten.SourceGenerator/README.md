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
* **Next milestone (iteration 3):** runtime first-call LINQ-parse plumbing. `QueryCompiler.BuildQueryPlan` already runs codegen-free; bypass `CompiledQuerySourceBuilder.AssembleTypes` and use the source-gen scaffold to hold the SQL + parameter binder. Memoize forever. Likely shape: a `[ModuleInitializer]` block emitted alongside the binder class registers the handler with a runtime registry keyed by query `Type`.
* **Then (iteration 4):** correctness + perf gates on three representative shapes (simple `Where`, `Where + OrderBy + Take`, `Where + Include<T>`) inside a **new isolated test harness** (`Marten.CompiledQueries.Tests`) running both the runtime-codegen and source-gen paths side-by-side. The scattered compiled-query tests currently in `LinqTests/Compiled`, `LinqTests/Bugs`, `LinqTests/Includes`, `LinqTests/ChildCollections`, `LinqTests/Acceptance`, `DocumentDbTests`, and `CoreTests` stay on the codegen path until the PoC lands green.
* **Final:** decision — continue Direction D for the rest of Marten, or fall back to packaging extraction for compiled queries specifically. If green, the scattered compiled-query tests migrate into the new harness as a follow-up, and the runtime codegen artifacts under each test project's `bin/.../Internal/Generated/CompiledQueries/` go away.

See [#4405](https://github.com/JasperFx/marten/issues/4405) for the full success / failure criteria and timeline.

## Discovery model

The generator is **gated by `[JasperFxAssembly]`** on the consumer assembly — without the marker, the generator emits nothing. Within a marked assembly, the generator finds every type implementing `ICompiledQuery<TDoc, TOut>` and emits scaffolding for it.

Discovery for other constructs (documents, ancillary stores) follows the same gating pattern with construct-specific attributes — see [#4404](https://github.com/JasperFx/marten/issues/4404) for the full attribute matrix.

## Packaging

Analyzer-only NuGet — no runtime payload. Consumers reference the package the same way they reference `Marten.Newtonsoft` (additive opt-in). The package's `IncludeBuildOutput=false` + `analyzers/dotnet/cs` packing path ensures the source generator runs at consumer compile time without bringing a runtime dependency.
