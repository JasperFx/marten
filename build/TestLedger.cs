using System;
using System.IO;
using Bobcat.Ledger;
using Bobcat.Supervisor;
using Nuke.Common;
using Nuke.Common.IO;
using Serilog;

// The duration ledger (JasperFx/bobcat#565, #730): Bobcat's TestLedger, one file per project and
// framework, read before a supervised run to balance its lanes and folded forward after it.
//
// It travels as a CI ARTIFACT, not a committed file. Each test job in .github/workflows/tests.yml
// downloads the `folded-test-ledgers` artifact of the newest successful master run into
// artifacts/previous-test-ledger/, balances on it, and writes its folded ledger to
// artifacts/test-ledger/durations/; the flakiness roll-up gathers those back into
// `folded-test-ledgers` for the next run. Nothing is committed, so a test run never produces a
// diff or a merge conflict. A run with no previous ledger (a laptop, or a branch whose master run
// has expired) balances lanes by test count, which is the supervisor's own fallback.
//
// Bobcat 1.0 keys the ledger and KnownDurations() by DISPLAY NAME itself (bobcat#730). xUnit v3's
// uid hashes the assembly path, so before that this file looked durations up by display name
// through a Supervisor.TestFilter hook and re-keyed every earlier observation onto the run's uids.
// Both workarounds are gone: Bobcat re-keys an older (uid-keyed, version 1/2) ledger when it loads,
// and the run report states how many durations matched ("Known durations matched N/M test(s)").
partial class Build
{
    /// <summary>
    /// A previous run's ledger to balance on (<c>--previous-test-ledger path/to/dir</c>). Defaults to
    /// artifacts/previous-test-ledger/, where CI puts the last master run's artifact.
    /// </summary>
    [Parameter] readonly AbsolutePath PreviousTestLedger;

    /// <summary>
    /// A test no run has seen for this long is dropped from the folded ledger: deleted or renamed.
    /// Filtered runs keep everything else, which is why this is by age and not by "not in this run".
    /// </summary>
    static readonly TimeSpan LedgerForgetsAfter = TimeSpan.FromDays(30);

    AbsolutePath PreviousTestLedgerDirectory => PreviousTestLedger ?? RootDirectory / "artifacts" / "previous-test-ledger";

    static string testLedgerFileName(string projectName, string framework) => $"{projectName}.{framework}.json";

    TestLedger loadTestLedger(string projectName, string framework)
    {
        var path = PreviousTestLedgerDirectory / testLedgerFileName(projectName, framework);

        try
        {
            // Load keeps the retention written in the file; merging into an empty ledger moves an
            // older artifact onto Bobcat's default (bobcat#724).
            return TestLedger.Empty().Merge(TestLedger.Load(path));
        }
        catch (Exception e)
        {
            // A corrupt or newer-schema ledger must never stop the tests: balance by count instead.
            Log.Warning(e, "Could not read the test ledger {Ledger} — balancing lanes by test count", path);
            return TestLedger.Empty();
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

            var now = DateTimeOffset.UtcNow;
            var updated = ledger
                .Record(SupervisorLedger.From(results, runId, now))
                .PruneTestsNotSeenSince(now - LedgerForgetsAfter);

            var artifact = LedgerDirectory / "durations" / testLedgerFileName(projectName, framework);
            artifact.Parent.CreateDirectory();
            updated.Save(artifact);
        }
        catch (Exception e)
        {
            // Reporting about the tests must never be what fails the tests.
            Log.Warning(e, "Could not record the test ledger for {Project}", projectName);
        }
    }
}
