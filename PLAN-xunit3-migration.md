# Migration Plan: xunit v2 → xunit v3

Target: `xunit` 2.9.3 → `xunit.v3` 3.2.2 across the Marten repo, keeping VSTest as the
execution mode so Nuke and the GitHub Actions workflows stay essentially unchanged.

## 1. Scope inventory (measured, not estimated)

| Surface | Count | Notes |
|---|---|---|
| Projects referencing `xunit` | 26 | includes `src/samples/Helpdesk/Helpdesk.Api.Tests` (separate solution) |
| Files with `[Fact]` / `[Theory]` | ~980 | the bulk needs **no** edits |
| Files with `using Xunit.Abstractions` | 82 | namespace removed in v3 — mechanical |
| `Task InitializeAsync` declarations | 144 | → `ValueTask` |
| `Task DisposeAsync` declarations | 185 | → `ValueTask`, now via `IAsyncDisposable` |
| `IAsyncLifetime` implementors | 131 files | |
| Custom `XunitTestFramework` subclasses | 5 | `src/TestSetup.cs`, DocumentDbTests, DaemonTests, LinqTests, Marten.Testing (+ Helpdesk) |
| Custom `FactDiscoverer` / `TheoryDiscoverer` | 2 | `SerializerTypeTargetedFact/Theory`, 20 call sites |
| `Record.Exception` uses | 10 | `Record.ExceptionAsync` now returns `ValueTask` |
| `async void` tests | 0 | nothing to fix (v3 fast-fails these) |

TFMs are already `net9.0;net10.0` (`Directory.Build.props:12`) — both above xunit v3's
net8.0 floor. Local SDK is 10.0.101; no `global.json`. No `xunit.runner.json` files exist.

`xunit.runner.visualstudio` is **already at 3.1.5**, which is the version that supports v3
under VSTest. That is the single biggest reason the CI story stays cheap.

## 2. Ranked risks / real blockers

### R1 — `IntegrationContext : IDisposable, IAsyncLifetime` (correctness landmine)

`src/Marten.Testing/Harness/IntegrationContext.cs:48` implements both. In v3
`IAsyncLifetime` inherits `IAsyncDisposable`, and **xunit calls only one of
`IDisposable` / `IAsyncDisposable`, never both** — it prefers the async one. Today
`DisposeAsync()` happens to call `Dispose()` (line ~187), so this specific class survives,
but the pattern is repeated in ~4 other files and any subclass that overrides
`Dispose()` without chaining will silently stop cleaning up sessions/connections. Given
that `IntegrationContext` is `Compile`-linked into ~12 test projects, a silent leak here
looks like flaky test-suite-wide connection exhaustion, not a compile error.

Action: collapse to a single async teardown path in the harness. Keep `Dispose()` only
where the class does *not* implement `IAsyncLifetime`. Audit every `override void Dispose`
in a class whose base is `IntegrationContext`/`DestructiveIntegrationContext`.

### R2 — the two custom discoverers

`SerializerTypeTargetedFact` / `SerializerTypeTargetedTheory` use the v2 extensibility model
that v3 deleted wholesale (`IAttributeInfo`, `IMessageSink`-ctor discoverers, `XunitTestCase`
with `TestMethodDisplay` args, string-based `[XunitTestCaseDiscoverer]`). These do not port —
they get rewritten. The good news: all 20 call sites are `RunFor = SerializerType.Newtonsoft`,
and v3 makes the discoverer unnecessary. Set `Skip` from the `RunFor` setter, which runs
during attribute construction:

```csharp
public sealed class SerializerTypeTargetedFact: FactAttribute
{
    private SerializerType _runFor;
    public SerializerType RunFor
    {
        get => _runFor;
        set
        {
            _runFor = value;
            if (value != TestsSettings.SerializerType)
                Skip = $"Test skipped as it cannot be run for {TestsSettings.SerializerType}";
        }
    }
}
```

Same shape for the `TheoryAttribute` variant. No `IFactAttribute` implementation, no
discoverer, no `virtual`-property dependency. (Alternative if we want it declarative:
v3's `SkipUnless`/`SkipType` pointing at a static `TestsSettings.IsNewtonsoft` bool.)

### R3 — the 5 `XunitTestFramework` subclasses

Each exists only to run one line at assembly startup:
`SerializerFactory.DefaultSerializerType = TestsSettings.SerializerType;`. v3 changed
`[assembly: TestFramework]` to a `typeof()` form and the framework base class entirely; the
runner-extensibility docs are still marked "forthcoming" upstream. Do not port it — replace
with a `[ModuleInitializer]` in one shared file `Compile`-linked like the rest of the harness:

```csharp
internal static class SerializerBootstrap
{
    [ModuleInitializer]
    internal static void Initialize() =>
        SerializerFactory.DefaultSerializerType = TestsSettings.SerializerType;
}
```

Delete `src/TestSetup.cs`, `src/{DocumentDbTests,DaemonTests,LinqTests,Marten.Testing}/TestSetup.cs`.
(`[assembly: AssemblyFixture(typeof(...))]` is the v3-native alternative if we ever need
ordering guarantees relative to discovery; the module initializer is simpler and enough here.)

### R4 — third-party packages bound to xunit v2

- `Neovolve.Logging.Xunit` 6.3.0 → **`Neovolve.Logging.Xunit.v3` 7.2.0** (different package id).
  Used by DaemonTests + DaemonTests.ManualOnly.
- `Ogooreck` 0.8.0 — xunit-v2-bound, used only by `src/samples/Helpdesk/Helpdesk.Api.Tests`.
  That sample is a **separate solution with pre-existing build failures**; explicitly out of
  scope for this migration. Leave it on v2 and note it.
- `Alba`, `Shouldly`, `NSubstitute`, `Bogus`, `FluentAssertions`, `Microsoft.Extensions.TimeProvider.Testing`
  are framework-agnostic — no action.
- `coverlet.collector` (10 projects) keeps working under VSTest. It would break under MTP —
  another reason to defer MTP.

### R5 — test-project → test-project `ProjectReference` chains

v3 test projects are executables. We have `CoreTests → DaemonTests`,
`DaemonTests → EventSourcingTests`, `Marten.Testing → EventSourcingTests`. Exe→Exe
references are legal and each assembly gets its own generated entry point, but this is the
most likely place to hit a surprise (duplicate entry point, `GenerateProgramFile`, output
copying). Validate early — see Phase 1 pilot.

### R6 — `build.cs` rewrites `AssemblyInfo.cs`

`SetupTestParallelization()` (`build/build.cs:547`) writes
`[assembly: CollectionBehavior(DisableTestParallelization = ...)]` into 8 projects. The
property form is unchanged in v3, so this keeps working. (The string-ctor form of
`CollectionBehavior`/`TestFramework`/`TestCaseOrderer` is what broke — we don't use it.)
Optional cleanup: switch to a checked-in `xunit.runner.json` with
`"parallelizeTestCollections": false`, which is v3-idiomatic and removes generated-source churn.

## 3. Phases

### Phase 0 — decision + spike ✅ DONE

Spike executed on `Marten.SourceGenerator.Tests` (2026-07-29), on branch
`chore/xunit-v3-migration` cut from `origin/master` @ `5f251a6e5` (9.20.2), in an isolated
worktree at `/Users/jeremymiller/code/_cw_worktrees/marten-xunit3`. **Result: green, and the
per-project cost for a project with no v2-specific API usage is two lines.**

Baselines were captured on that same commit *before* the change (xunit v2) and compared
against the same commit *after* (xunit v3), so the comparison is clean of any other drift.

What the spike changed:

- `Directory.Packages.props`: added `xunit.v3` 3.2.2 **alongside** the existing `xunit` 2.9.3.
- `Marten.SourceGenerator.Tests.csproj`: `<PackageReference Include="xunit" />` →
  `xunit.v3`, plus `<OutputType>Exe</OutputType>`.
- **Zero source-file changes.**

What it proved:

| Question | Answer |
|---|---|
| Do v2 and v3 projects coexist in one solution build? | **Yes** — `dotnet build src/Marten.slnx -c Release` → 0 errors. This is what makes the incremental per-project rollout viable. |
| Same test results? | **Exact match on both TFMs.** v2 baseline and v3 result are each 19 total / 18 passed / 1 failed, on both net10.0 and net9.0. The one failure, `emits_handler_class_per_discovered_compiled_query`, is a pre-existing Shouldly assertion on generator output that fails identically on v2 on this same master commit — unrelated to xunit. |
| Does VSTest `dotnet test` still work unchanged? | **Yes** — same invocation, same `--framework`, same console output shape, `--no-build` fine. |
| Is it genuinely v3? | Yes — `xunit.v3.{core,assert,common,runner.*}.dll` in output, and a native Mach-O `arm64` apphost is produced. |
| Does the standalone-exe runner work? | Yes — `./Marten.SourceGenerator.Tests` self-executes with v3's own reporter (`Total: 19, Failed: 1`). Free, and it's the on-ramp if we ever adopt MTP. |

**New finding — `xUnit1051` will flood the build.** The v3 analyzers add a rule requiring
`TestContext.Current.CancellationToken` be passed to any method accepting a
`CancellationToken`. It fired 4× in this trivial, DB-free project. A rough count of no-arg
async calls in just six of the DB-heavy suites (CoreTests, DocumentDbTests,
EventSourcingTests, LinqTests, DaemonTests, TenantPartitionedEventsTests) is **~5,650**
candidate sites — 2,955 of them `SaveChangesAsync()`.

Action: add `<NoWarn>$(NoWarn);xUnit1051</NoWarn>` to the shared test props in Phase 1.
Threading real cancellation tokens through the daemon tests is a genuine improvement, but
it is a separate opt-in effort and must not be coupled to this migration.

### Phase 0b — remaining prep

- Confirm **VSTest mode, not MTP**, for this migration. xunit v3 supports both; staying on
  VSTest means `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` 3.1.5 stay, `dotnet test`
  semantics stay, Nuke's `DotNetTest` calls stay, coverlet keeps working, and the 8 CI
  workflows need no runner rework. — *validated by the spike.*
- Decide the three open questions in §6 before Phase 1 starts (Helpdesk, `xunit.runner.json`,
  `global.json`), since all three touch shared files.

### Phase 1 findings (executed 2026-07-29)

Four things the up-front analysis got wrong or missed. All are recorded here because they
change the plan, not just the code.

**F1 — the per-project rollout in Phase 3 is not possible for the bulk.** 22 of the 27
xunit projects form a single *atomic cohort* that must convert in one commit: 7 projects
`Compile`-link xunit-typed harness files out of `Marten.Testing` (changing
`IntegrationContext`'s signatures breaks every linker at once), and test-project →
test-project `ProjectReference` chains pull in the rest. Only 5 can move independently.
Non-test projects that link harness files only take `ConnectionSource.cs` and document
POCOs, so they are unaffected.

**F2 — R1 was real, and the compiler does not catch it.** 42 classes inherit an
`IDisposable`-only harness base *and* implement `IAsyncLifetime`. Under v2 the runner
called both; under v3 `IAsyncLifetime` inherits `IAsyncDisposable` and only the async path
runs, so the base `Dispose()` would silently stop disposing sessions. The fix was to give
`OneOffConfigurationsContext` / `StoreContext<T>` a virtual async teardown that chains to
`Dispose()`, then convert 92 derived members to `override` + `base.DisposeAsync()`.
Note the hiding warnings (CS0114/CS0108) only appear on a **full** rebuild — an
incremental build reports zero and looks clean.

**F3 — `xunit.v3` build assets flow transitively.** Its entry-point generation ships in
`buildTransitive/`, so a *non-test* project that `ProjectReference`s a test project gets a
generated `Main` injected (CS0017). Hit `EventSourceWorker`, which has no xunit reference
at all. Fixed with `XunitAutoGeneratedEntryPoint=false` there. Separately,
`Microsoft.NET.Test.Sdk` emits its own VSTest `Program.cs` and an MTP entry point, both of
which collide with xunit's — hence `GenerateProgramFile` / `GenerateTestingPlatformEntryPoint`
false in `src/Tests.props`.

**F4 — serializer targeting never worked under v2.** `SerializerTypeTargetedFact`'s v2
discoverer read `GetNamedArgument<SerializerType?>` against a non-nullable enum property,
so it always saw null and never skipped. Every `RunFor = Newtonsoft` test has therefore
been *running* under the SystemTextJson CI leg — and passing. The v3 rewrite makes the
attribute do what it says, so those tests now skip under SystemTextJson (PatchingTests:
122 passed/1 skipped → 119 passed/4 skipped). **This needs a product decision:** either
accept the (correct) skips and lose that STJ coverage, or drop the now-evidently-stale
`RunFor` annotations, since the tests demonstrably pass under both serializers. Not
decided here.

**F5 — v2 was silently *dropping* tests, not just failing to skip them.** F4 understated
this. There were two distinct v2 defects, depending on how a project consumed the harness:

- Projects that **`Compile`-link** `SerializerTypeTargetedFact.cs` (DocumentDbTests,
  LinqTests, CoreTests, EventSourcingTests) got the attribute type in their *own* assembly,
  while `[XunitTestCaseDiscoverer(..., "Marten.Testing")]` pointed the discoverer at a
  different assembly. v2 could not resolve it and **silently discovered no test at all**.
  Confirmed by diffing `--list-tests`: DocumentDbTests discovers **1082 → 1086** tests
  under v3, and the 4 additions are exactly its 4 `SerializerTypeTargetedFact` methods.
  Expect the same for LinqTests' 9.
- Projects that only **`ProjectReference`** Marten.Testing (PatchingTests) resolved the
  discoverer, but it read `GetNamedArgument<SerializerType?>` against a non-nullable enum
  property, always saw null, and never skipped — so those tests ran under *both*
  serializers.

Net effect: **13 tests (4 DocumentDbTests + 9 LinqTests) have never run in CI**, and 3
more ran under a serializer they were annotated not to support. v3 restores all of them.

Restoring them is not free: they change execution order and add data to the shared
`integration` store, which is what surfaced the `delete_many_documents_by_query`
order-dependency below. Expect a similar tail in LinqTests.

**F6 — DaemonTests' `TestFramework` attribute has been dead since a project rename.** It
declared `TestFramework("Marten.AsyncDaemon.TestSetup", "Marten.AsyncDaemon")`, but the type
is `Marten.AsyncDaemon.Testing.TestSetup` in assembly `DaemonTests` — both the type name and
the assembly name are wrong, so xunit v2 could never load it. Consequence:
`SerializerFactory.DefaultSerializerType` (which defaults to **SystemTextJson**) was never
overridden there, so **DaemonTests has always run under SystemTextJson regardless of
`DEFAULT_SERIALIZER`**, including in the Newtonsoft CI leg.

Replacing it with a working `[ModuleInitializer]` flipped the suite to Newtonsoft and broke
2 tests (`Bug_5041_natural_key_source_discovery`, which cannot round-trip an
`Enumerable.AppendPrepend1Iterator` property through Newtonsoft). A framework migration must
not silently change which serializer a suite runs under, so `src/DaemonTests/TestSetup.cs`
was **deleted** rather than repaired — that preserves v2 behaviour exactly. The other three
projects' attributes were verified correct and did get working module initializers.

**Open decision:** whether DaemonTests *should* honour `DEFAULT_SERIALIZER`. Turning it on
is a one-file change, but it requires fixing the 2 Bug_5041 failures first.

### Phase 1 — harness + shared infrastructure (the hard part)

Order matters; everything downstream depends on this landing first.

1. `Directory.Packages.props`: `xunit` 2.9.3 → `xunit.v3` 3.2.2; add
   `Neovolve.Logging.Xunit.v3` 7.2.0; keep `xunit.runner.visualstudio` 3.1.5 and
   `Microsoft.NET.Test.Sdk` 18.0.1. Keep the old `xunit` entry until Helpdesk is dealt with.
2. Add `src/Tests.props` (or equivalent) with `<OutputType>Exe</OutputType>` and import it
   from the 25 in-solution test projects, rather than hand-editing `OutputType` 25 times.
3. Rewrite `SerializerTypeTargetedFact.cs` + `SerializerTypeTargetedTheory.cs` (R2).
4. Add the shared `SerializerBootstrap` module initializer, delete the 5 `TestSetup.cs` (R3).
   Add the new file to the `<Compile Include="..\Marten.Testing\Harness\...">` lists in
   CoreTests / EventSourcingTests / LinqTests / DocumentDbTests / etc.
5. Fix the harness lifecycle (R1): `IntegrationContext`, `DestructiveIntegrationContext`,
   `DefaultStoreFixture`, `StoreFixture`, `StoreContext<T>`, `OneOffConfigurationsContext`.
6. Verify `SessionTypesAttribute : ClassDataAttribute` still compiles — v3 kept
   `ClassDataAttribute` and still accepts `IEnumerable<object[]>` sources, but this is a
   derived attribute and worth an explicit check.
7. Pilot: get `Marten.Testing` + `CoreTests` green on net10.0 locally.

### Phase 2 — mechanical sweep

Scriptable, low-judgement, do it in one commit per concern so review is tractable:

- Delete `using Xunit.Abstractions;` (82 files). Add `using Xunit;` where `ITestOutputHelper`
  was the only reason for the using.
- `Task InitializeAsync()` → `ValueTask InitializeAsync()` (144).
- `Task DisposeAsync()` → `ValueTask DisposeAsync()` (185); drop `Task.CompletedTask`
  returns for `ValueTask.CompletedTask`; adjust `override` chains.
- Any class implementing `IAsyncLifetime` **and** `IDisposable`: fold the sync cleanup into
  `DisposeAsync` (R1).
- `Record.ExceptionAsync` call sites (subset of the 10) → `await` a `ValueTask`.

Let the xunit v3 analyzers (shipped with `xunit.v3`) drive the tail of this — they catch
most remaining v2-isms. Note the repo currently suppresses `xUnit1013` in 10 projects and
`xUnit1031` in one; keep those suppressions initially and revisit after green.

### Phase 3 — per-project rollout

One PR-sized batch at a time, each verified independently. **One project and one TFM at a
time** (the DB-backed suites collide on the shared `marten_testing` database):

1. Marten.Testing, CoreTests (done in Phase 1 pilot)
2. DocumentDbTests, LinqTests, PatchingTests, CompiledQueryTests
3. EventSourcingTests, DaemonTests, DaemonTests.ManualOnly (+ Neovolve v3 swap)
4. TenantPartitionedEventsTests, MultiTenancyTests, MultiHostTests, ContainerScopedProjectionTests
5. ValueTypeTests, ModularConfigTests, StressTests, Marten.SourceGenerator.Tests
6. Extensions: Marten.NodaTime.Testing, Marten.AspNetCore.Testing, Marten.PostGIS.Tests,
   Marten.PgVector.Tests, Marten.MemoryPack.Tests, Marten.EntityFrameworkCore.Tests
7. Deferred: `src/samples/Helpdesk/Helpdesk.Api.Tests` (Ogooreck), `src/samples/DocSamples`

### Phase 4 — build + CI

See §4. Do this **after** at least batch 1–3 are green locally, so CI isn't the discovery
mechanism for framework problems.

### Phase 5 (optional, separate effort) — Microsoft Testing Platform

Not part of this migration. Would mean `<UseMicrosoftTestingPlatformRunner>` /
`<TestingPlatformDotnetTestSupport>`, adding `Microsoft.Testing.Extensions.Telemetry`
(required from v3.2.0), replacing coverlet with the MTP code-coverage extension, and
reworking every `dotnet test`/`DotNetTest` invocation's arguments. Revisit once v3 is
settled.

## 4. CI runner changes

**Headline: staying on VSTest keeps this small.** Concretely:

| Item | Change needed? |
|---|---|
| `actions/setup-dotnet` installing 9.0.x + 10.0.x | **No.** v3 test exes need the matching runtime; both are already installed. |
| `./build.sh test-*` targets (Nuke `DotNetTest`) | **No.** VSTest mode; `--no-build --no-restore --framework` all still valid. |
| `daemon.yml`'s raw `dotnet test ... --logger "console;verbosity=detailed"` | **No.** VSTest logger syntax unchanged. Would break under MTP. |
| `DISABLE_TEST_PARALLELIZATION` env → `build.cs:547` AssemblyInfo generation | **No**, property form of `CollectionBehavior` is unchanged. Optional cleanup to `xunit.runner.json`. |
| `timeout-minutes: 20` on all 8 workflows | **Probably yes.** v3 launches each test assembly as its own process and startup/discovery cost shifts. Bump to 30 on the first v3 run, then tune back down with real numbers. |
| `./build.sh compile` (whole-solution build) | **No**, but expect first-run restore churn from the package-id change; nothing workflow-level. |
| `global.json` | Not required (SDK 10.0.101 ≫ v3's floor), but worth adding to pin the SDK so v3's exe-based runs are reproducible across runners. Judgement call, not a blocker. |
| coverlet in 10 projects | **No** under VSTest. Breaks under MTP. |

The one workflow that deserves a second look is `on-push-do-ci-build-postgis-pgvector.yml`
(it calls the PascalCase Nuke target names `TestPostGIS`/`TestPgVector`) — unaffected by
xunit, just noting it's the odd one out if targets get touched.

Sequencing for CI: land Phase 1–3 behind a branch, let the 8 push/PR workflows run on that
branch once (they all trigger on `pull_request` to master), read the timings, then adjust
`timeout-minutes` in the same PR.

## 5. Verification

- Per project: `dotnet test src/<Project> --framework net10.0 --configuration Release`,
  then repeat for net9.0. Never both TFMs in one invocation — they collide on the shared DB.
- Compare **test counts** before/after per project, not just pass/fail. The most likely
  silent regression is tests quietly disappearing from discovery (the discoverer rewrite and
  the `TestFramework` removal are both discovery-time changes).
- Specifically assert that the 20 `SerializerTypeTargetedFact` tests are *skipped* under
  `DEFAULT_SERIALIZER=SystemTextJson` and *run* under Newtonsoft — that's the behaviour the
  deleted discoverers encoded, and both CI serializer matrices exercise it.
- Watch for connection-pool exhaustion / hung suites as the signature of an R1 regression.

## 6. Open questions

1. Helpdesk sample: leave on xunit v2 (keeps a second `xunit` package version in
   `Directory.Packages.props`), or drop it from the migration entirely / retire it?
2. Adopt `xunit.runner.json` and delete `SetupTestParallelization()` from `build.cs`, or
   keep the generated-`AssemblyInfo.cs` mechanism?
3. Add a `global.json` SDK pin as part of this, or keep floating?

## References

- [Migrating Unit Tests from v2 to v3](https://xunit.net/docs/getting-started/v3/migration)
- [Migrating Extensibility from v2 to v3](https://xunit.net/docs/getting-started/v3/migration-extensibility)
- [What's New in v3](https://xunit.net/docs/getting-started/v3/whats-new)
- [xunit v3 + Microsoft Testing Platform](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
- [xunit.v3 3.2.2 on NuGet](https://www.nuget.org/packages/xunit.v3)
- [Neovolve.Logging.Xunit.v3 7.2.0](https://www.nuget.org/packages/Neovolve.Logging.Xunit.v3)
