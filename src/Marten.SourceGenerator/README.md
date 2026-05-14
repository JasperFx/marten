# Marten.SourceGenerator (PoC initialized — tracking issue [#4405](https://github.com/JasperFx/marten/issues/4405))

Source generator package for Marten. Eliminates runtime code generation (no `JasperFx.RuntimeCompiler`, no `Roslyn` at runtime, no `FastExpressionCompiler`) for the constructs it covers. AOT-publishing-ready.

## PoC scope (current)

**Validates the compiled-query source-gen + first-call-LINQ-parse + memoize model.** Compiled queries are the highest-risk-of-surprise work stream under the zero-runtime-codegen direction tracked in [#4404](https://github.com/JasperFx/marten/issues/4404). If the seam works for compiled queries, the rest of Direction D (document storage, event storage, ancillary stores) becomes a comparatively mechanical exercise. If not, compiled queries fall back to packaging extraction.

## Status

* **PoC initialized.** Project scaffolds; one incremental generator stub fires for every `ICompiledQuery<TDoc, TOut>` implementation in assemblies marked with `[JasperFxAssembly]`. Currently emits a marker class per discovered type — proves the pipeline.
* **Next milestone:** typed parameter binder emission. Replace the marker with a `static class {QueryType}_CompiledQueryHandler` that exposes `BindParameters(query, parameters[])` with direct property reads.
* **Then:** runtime first-call LINQ-parse plumbing. `QueryCompiler.BuildQueryPlan` already runs codegen-free; bypass `CompiledQuerySourceBuilder.AssembleTypes` and use the source-gen scaffold to hold the SQL + parameter binder. Memoize forever.
* **Then:** correctness + perf gates on three representative shapes (simple `Where`, `Where + OrderBy + Take`, `Where + Include<T>`).
* **Final:** decision — continue Direction D for the rest of Marten, or fall back to packaging extraction for compiled queries specifically.

See [#4405](https://github.com/JasperFx/marten/issues/4405) for the full success / failure criteria and timeline.

## Discovery model

The generator is **gated by `[JasperFxAssembly]`** on the consumer assembly — without the marker, the generator emits nothing. Within a marked assembly, the generator finds every type implementing `ICompiledQuery<TDoc, TOut>` and emits scaffolding for it.

Discovery for other constructs (documents, ancillary stores) follows the same gating pattern with construct-specific attributes — see [#4404](https://github.com/JasperFx/marten/issues/4404) for the full attribute matrix.

## Packaging

Analyzer-only NuGet — no runtime payload. Consumers reference the package the same way they reference `Marten.Newtonsoft` (additive opt-in). The package's `IncludeBuildOutput=false` + `analyzers/dotnet/cs` packing path ensures the source generator runs at consumer compile time without bringing a runtime dependency.
