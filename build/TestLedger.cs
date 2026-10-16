using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bobcat.Ledger;
using Bobcat.Supervisor;
using Nuke.Common;
using Nuke.Common.IO;
using Serilog;

// The committed duration ledger (JasperFx/bobcat#565): Bobcat's TestLedger, one file per project
// and framework under build/test-ledger/, read before a supervised run to balance its lanes and
// folded forward after it.
//
// Why committed rather than fetched from the previous CI run: a lane balancer with no durations
// falls back to test COUNT, and Marten's classes differ in cost by two orders of magnitude.
// Wolverine measured a count-balanced fleet finishing its lanes at 101.5s and 11.4s.
//
// KEYED BY DISPLAY NAME, NOT BY UID — and that is load-bearing. Bobcat keys the ledger and
// Supervisor.KnownTestDurations by the test's MTP uid, and xUnit v3 derives that uid from a hash
// that includes the test ASSEMBLY'S PATH. The same test therefore has a different uid on every
// checkout path, configuration (bin/Debug vs bin/Release) and target framework (bin/.../net9.0 vs
// net10.0). The first CI run of this PR proved it: a ledger committed from a local Debug run
// matched 0 of 2525 uids on the Release CI runner, and the lanes silently fell back to the
// count-balanced split. So the durations are looked up by display name (namespace, class, method
// and theory arguments, which is a function of the code alone) and mapped onto this run's uids as
// discovery reports them, and the fold re-keys earlier observations onto this run's uids by
// display name before recording, so the ledger never accumulates one copy per machine.
//
// Nothing here writes the committed file implicitly. Every run writes its folded ledger to
// artifacts/test-ledger/durations/ (uploaded with the job, and gathered by the roll-up job as the
// `folded-test-ledgers` artifact); a person refreshes the committed copy on purpose with
// `--update-test-ledger`, or by copying a CI artifact's file over it. A ledger that quietly
// rewrote itself on every local run would turn every developer's machine speed into a diff.
partial class Build
{
    /// <summary>
    /// Writes each supervised run's folded ledger back to build/test-ledger/ (<c>--update-test-ledger</c>)
    /// instead of only to the artifacts directory. For refreshing the committed durations.
    /// </summary>
    [Parameter] readonly bool UpdateTestLedger;

    /// <summary>
    /// Observations kept per test. Small on purpose: the ledger is committed, the balancer only
    /// needs a median, and Marten's larger suites have thousands of tests each.
    /// </summary>
    const int LedgerRunsPerTest = 3;

    static AbsolutePath CommittedTestLedgerDirectory => RootDirectory / "build" / "test-ledger";

    static string testLedgerFileName(string projectName, string framework) => $"{projectName}.{framework}.json";

    static TestLedger loadTestLedger(string projectName, string framework)
    {
        var path = CommittedTestLedgerDirectory / testLedgerFileName(projectName, framework);

        try
        {
            return File.Exists(path) ? TestLedger.Load(path, LedgerRunsPerTest) : TestLedger.Empty(LedgerRunsPerTest);
        }
        catch (Exception e)
        {
            // A corrupt or newer-schema ledger must never stop the tests: balance by count instead.
            Log.Warning(e, "Could not read the test ledger {Ledger} — balancing lanes by test count", path);
            return TestLedger.Empty(LedgerRunsPerTest);
        }
    }

    /// <summary>
    /// The ledger's durations by display name: the median measured cost of every observation that
    /// carries that name, whatever uid it was recorded under.
    /// </summary>
    static IReadOnlyDictionary<string, TimeSpan> durationsByDisplayName(TestLedger ledger)
    {
        return ledger.Tests.Values
            .SelectMany(runs => runs)
            .Where(run => run.TotalMs is not null && !string.IsNullOrEmpty(run.DisplayName))
            .GroupBy(run => run.DisplayName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var sorted = group.Select(run => run.TotalMs!.Value).OrderBy(x => x).ToArray();
                    var middle = sorted.Length / 2;
                    var median = sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
                    return TimeSpan.FromMilliseconds(median);
                },
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Maps the ledger's durations onto this run's uids as discovery reports them.
    /// </summary>
    /// <remarks>
    /// Bobcat has no hook between discovery and lane planning, so this rides on
    /// <c>Supervisor.TestFilter</c>, which sees every discovered test before the plan is built and
    /// keeps all of them. <c>KnownTestDurations</c> is the same dictionary instance, read when the
    /// plan is built, so by then it holds this run's uids.
    /// </remarks>
    sealed class LaneDurations
    {
        readonly IReadOnlyDictionary<string, TimeSpan> _byDisplayName;
        readonly Dictionary<string, TimeSpan> _byUid = new(StringComparer.Ordinal);

        public LaneDurations(TestLedger ledger) => _byDisplayName = durationsByDisplayName(ledger);

        public int Available => _byDisplayName.Count;
        public int Matched => _byUid.Count;
        public int Discovered { get; private set; }

        public IReadOnlyDictionary<string, TimeSpan> KnownTestDurations => _byUid;

        public bool Observe(WorkerTest test)
        {
            Discovered++;
            if (_byDisplayName.TryGetValue(test.DisplayName, out var duration)) _byUid[test.Uid] = duration;
            return true;
        }
    }

    void recordTestLedger(TestLedger ledger, string projectName, string framework, SupervisorResults results)
    {
        try
        {
            // A run that aborted or discovered nothing has no durations worth keeping.
            if (results.Tests.Count == 0 || results.AbortReason is not null) return;

            // The caller owns the run id and the clock, so the fold stays deterministic. In CI the
            // run id names the workflow run, which is what you would look up to explain an entry.
            var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { Length: > 0 } githubRun
                ? $"gh-{githubRun}-{Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1"}"
                : $"local-{Guid.NewGuid():N}";

            // Re-key the earlier observations onto this run's uids by display name (see the header):
            // without it every machine, configuration and checkout path adds a full second copy of
            // the suite. A test this run did not see is dropped — deleted, renamed, or filtered out.
            var uidsByName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var test in results.Tests) uidsByName.TryAdd(test.DisplayName, test.Uid);

            var earlier = ledger.Tests.Values
                .SelectMany(runs => runs)
                .Where(run => uidsByName.ContainsKey(run.DisplayName))
                .Select(run => run with { Uid = uidsByName[run.DisplayName] });

            var updated = TestLedger.Empty(LedgerRunsPerTest)
                .Record(earlier)
                .Record(SupervisorLedger.From(results, runId, DateTimeOffset.UtcNow));

            var artifact = LedgerDirectory / "durations" / testLedgerFileName(projectName, framework);
            artifact.Parent.CreateDirectory();
            updated.Save(artifact);

            if (UpdateTestLedger)
            {
                var committed = CommittedTestLedgerDirectory / testLedgerFileName(projectName, framework);
                committed.Parent.CreateDirectory();
                updated.Save(committed);
                Log.Information("  updated the committed test ledger {Ledger}", RootDirectory.GetRelativePathTo(committed));
            }
        }
        catch (Exception e)
        {
            // Reporting about the tests must never be what fails the tests.
            Log.Warning(e, "Could not record the test ledger for {Project}", projectName);
        }
    }
}
