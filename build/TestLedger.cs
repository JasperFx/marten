using System;
using System.IO;
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
// falls back to test COUNT, and Marten's classes differ in cost by two orders of magnitude (a
// fifty-test LINQ class runs in a second, one daemon class takes a minute). Wolverine measured a
// count-balanced fleet finishing its lanes at 101.5s and 11.4s. The ledger is merge-friendly by
// design (union by test + run, deterministic prune, canonical serialization), so two branches that
// both refreshed it resolve by load-both-sides -> Merge -> save.
//
// Nothing here writes the committed file implicitly. Every run writes its updated ledger beside the
// retry ledger (artifacts/test-ledger/durations/, uploaded with the job's artifact); a person
// refreshes the committed copy on purpose with `--update-test-ledger`, or by copying a CI
// artifact's file over it. A ledger that quietly rewrote itself on every local run would turn every
// developer's machine speed into a diff.
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
            if (!File.Exists(path)) return TestLedger.Empty(LedgerRunsPerTest);

            var ledger = TestLedger.Load(path, LedgerRunsPerTest);
            Log.Information("  balancing lanes with {Count} test duration(s) from {Ledger}",
                ledger.KnownDurations().Count, RootDirectory.GetRelativePathTo(path));
            return ledger;
        }
        catch (Exception e)
        {
            // A corrupt or newer-schema ledger must never stop the tests: balance by count instead.
            Log.Warning(e, "Could not read the test ledger {Ledger} — balancing lanes by test count", path);
            return TestLedger.Empty(LedgerRunsPerTest);
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

            var updated = ledger.Record(SupervisorLedger.From(results, runId, DateTimeOffset.UtcNow));

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
