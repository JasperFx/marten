using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bobcat.Resilience;
using Bobcat.Supervisor;
using Nuke.Common;
using Nuke.Common.IO;
using Serilog;

// The supervised test runner (#5096): every test target drives its project's
// Microsoft.Testing.Platform host through Bobcat's Supervisor
// (https://github.com/JasperFx/bobcat) instead of shelling out to `dotnet test`.
//
// What that buys, and why it is here:
//
//   - Honest retries. A failing test is retried in a FRESH process — the reset bracket xUnit
//     cannot give you in-process, because a failed first attempt leaves half-built stores,
//     schema objects and daemon state behind. Crucially, a pass-on-retry is NEVER folded into
//     a clean pass: it lands in the flakiness ledger (see RetryLedger.cs) and in the job
//     summary. A crashed worker is Indeterminate, with its exit code and stderr, rather than a
//     silent pass or an ordinary "failed".
//
//   - Per-test knobs for the timing-sensitive suites. The supervisor reads xUnit traits:
//     [Trait("Retry", "3")] raises a single test's attempt ceiling and [Trait("Isolated",
//     "true")] runs it alone. That is the tool for a test that is genuinely racy against a
//     shared database, instead of deleting it or leaving the whole project out of CI —
//     which is how the seven projects in #5096 went dark in the first place.
//
// Parallelism in this repo is at the JOB level, not the worker level: .github/workflows/tests.yml
// gives every test project its own runner and its own Postgres. So the supervisor runs one worker
// here. Raising MaxParallelWorkers would need per-lane database isolation first (Marten's suites
// hard-code schema names, so the isolation has to be the catalog) plus a pass over the two traps
// in https://github.com/JasperFx/bobcat/blob/main/docs/parallel-ready-suites.md, neither of which
// has been checked against Marten. Deliberately left for a follow-up rather than half-wired here.
//
// The executable this drives comes from <UseMicrosoftTestingPlatformRunner> in src/Tests.props.
partial class Build
{
    /// <summary>
    /// Turns off the retry policy (<c>--disable-test-retry</c>). Worth reaching for when you are
    /// measuring a suite's real stability: a retry budget masks exactly the instability you are
    /// trying to quantify.
    /// </summary>
    [Parameter] readonly bool DisableTestRetry;

    /// <summary>
    /// Retries a whole project run may spend before every remaining failure is reported as failed
    /// WITHOUT being retried at all. Read the retry ledger against this number: a run sitting at
    /// the cap is not "N flaky tests", it is N flaky tests plus an unknown tail.
    /// </summary>
    const int MaxRetriesPerRun = 25;

    /// <summary>
    /// Attempts any one test may take. Three, not two: a first attempt inside a suite that has
    /// already churned the database is a materially different run from a fresh-process attempt,
    /// so a genuinely racy test deserves the same two clean shots the old ad-hoc reruns gave it.
    /// A per-test <c>[Trait("Retry", "N")]</c> raises this for that test alone.
    /// </summary>
    const int MaxAttemptsPerTest = 3;

    /// <summary>
    /// Runs one test project through the supervisor, once per target framework.
    /// </summary>
    /// <param name="projectPath">Repo-relative path to the .csproj.</param>
    /// <param name="frameworkOverride">
    /// Pins the run to one TFM regardless of <c>--framework</c>. Only for a project whose suite
    /// is meaningful on a single framework.
    /// </param>
    void RunTestProject(string projectPath, string frameworkOverride = null)
    {
        var failed = new List<string>();

        foreach (var framework in frameworksBuiltFor(projectPath, frameworkOverride))
        {
            if (!runSupervised(projectPath, framework)) failed.Add($"{projectPath} ({framework})");
        }

        if (failed.Count > 0) throw new InvalidOperationException($"Tests failed: {string.Join(", ", failed)}");
    }

    bool runSupervised(string projectPath, string framework)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var executable = testHostFor(projectPath, framework);

        Log.Information("=== {Project} ({Framework}): supervised run{Retry} ===",
            projectName, framework, DisableTestRetry ? ", retries OFF" : "");

        var supervisor = new Supervisor(new MtpWorkerFactory(executable))
        {
            MaxParallelWorkers = 1,
            RetryBudget = DisableTestRetry
                ? RetryBudget.None
                : new RetryBudget { MaxAttemptsPerTest = MaxAttemptsPerTest, MaxRetriesPerRun = MaxRetriesPerRun },
            Log = message => Log.Information("  {Message}", message)
        };

        if (!DisableTestRetry) supervisor.AddFailurePolicy(new RetryFailuresInFreshProcess());

        var results = supervisor.Run().GetAwaiter().GetResult();

        return report(projectName, framework, results);
    }

    /// <summary>
    /// Every failure gets another attempt in a fresh process, within the budget. Fresh-process
    /// isolation is the whole point: a Marten test that fails partway through has usually left a
    /// store, a schema or a running daemon behind it, and a warm retry then fails for a reason
    /// that has nothing to do with the original defect. A pass on any retry is reported as flaky,
    /// never as clean.
    /// </summary>
    class RetryFailuresInFreshProcess : IFailurePolicy
    {
        public Disposition Decide(AttemptContext attempt)
        {
            if (attempt.Succeeded || !attempt.RetriesAvailable) return null;

            return Disposition.RetryInFreshProcess(
                "a failure is retried in a fresh process, within the budget, to separate flaky from broken");
        }
    }

    bool report(string projectName, string framework, SupervisorResults results)
    {
        recordLedger(projectName, framework, results);

        if (results.AbortReason is not null)
        {
            Log.Error("=== {Project}: run ABORTED — {Reason} ===", projectName, results.AbortReason);
            return false;
        }

        if (results.Tests.Count == 0)
        {
            // Every project in the matrix has tests, so discovering none means something broke —
            // a half-built assembly, MTP discovery failing, a test host that came up wrong. Zero
            // tests is the one result that looks identical to a clean pass from the outside, and
            // a 30-job matrix where a job can quietly run nothing is the failure this whole
            // workflow exists to stop. Fail loudly instead.
            Log.Error("=== {Project}: NO tests were discovered — the run covered nothing ===", projectName);
            return false;
        }

        Log.Information("=== {Project}: {Summary} ===", projectName, results.Summarize());

        foreach (var flaky in results.PassedOnRetry)
            Log.Warning("  [FLAKY] {Test} — passed on attempt {Attempts}", flaky.DisplayName, flaky.AttemptCount);

        foreach (var fault in results.WorkerFaults) Log.Error("  [WORKER FAULT] {Fault}", fault);

        foreach (var test in results.Indeterminate)
            Log.Error("  [INDETERMINATE] {Test} — {Error}", test.DisplayName, test.Final.Outcome.ErrorMessage);

        foreach (var test in results.Failed)
            Log.Error("  [FAILED] {Test} — {Error}", test.DisplayName, test.Final.Outcome.ErrorMessage);

        return results.ExitCode == 0;
    }

    // ─── Test host resolution ──────────────────────────────────────────

    /// <summary>
    /// The frameworks a supervised run should cover: <c>--framework</c> (or the target's own
    /// override) when given, otherwise every TFM the project was actually built for — matching
    /// the bare <c>dotnet test</c> this replaced, which ran all of them.
    /// </summary>
    IReadOnlyList<string> frameworksBuiltFor(string projectPath, string frameworkOverride)
    {
        var chosen = frameworkOverride ?? Framework;
        if (!string.IsNullOrEmpty(chosen)) return [chosen];

        var binDir = RootDirectory / Path.GetDirectoryName(projectPath) / "bin" / Configuration;
        if (!Directory.Exists(binDir))
            throw new InvalidOperationException($"{binDir} does not exist — build {projectPath} first.");

        var frameworks = Directory.GetDirectories(binDir)
            .Select(Path.GetFileName)
            .Where(tfm => File.Exists(testHostPathFor(projectPath, tfm)))
            .OrderBy(tfm => tfm)
            .ToList();

        if (frameworks.Count == 0)
            throw new InvalidOperationException(
                $"No test host executable under {binDir}. Build the project first; the executable comes " +
                "from UseMicrosoftTestingPlatformRunner in src/Tests.props.");

        return frameworks;
    }

    string testHostFor(string projectPath, string framework)
    {
        var executable = testHostPathFor(projectPath, framework);
        if (!File.Exists(executable))
            throw new InvalidOperationException(
                $"{executable} does not exist — build {projectPath} for {framework} first. A build WITHOUT " +
                "UseMicrosoftTestingPlatformRunner yields a non-supervisable executable at the same path, " +
                "so prefer fixing the build over deleting this check.");

        return executable;
    }

    string testHostPathFor(string projectPath, string framework)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var host = RootDirectory / Path.GetDirectoryName(projectPath) / "bin" / Configuration / framework / projectName;
        return OperatingSystem.IsWindows() ? host + ".exe" : host;
    }
}
