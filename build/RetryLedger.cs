using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Bobcat.Supervisor;
using Nuke.Common.IO;
using Serilog;

// The retry ledger: what every supervised run reports about its OWN flakiness, somewhere a person
// will actually see it. Part of #5096.
//
// The problem it exists for: nothing in the GitHub UI distinguishes a job that passed cleanly from
// a job that passed after retrying nine tests. Both render as a green tick, and a Serilog warning
// buried in the log of a job that PASSED is a warning nobody has a reason to go read. Without this,
// "we added retries" quietly becomes "we stopped being able to tell whether the suite works" —
// which is the failure mode that put seven projects outside CI in the first place.
//
// Three outputs, deliberately:
//
//   - `$GITHUB_STEP_SUMMARY` markdown, so the numbers are on the run page without opening a log.
//   - a `::warning` workflow command when the retry count is nonzero, so it reaches the
//     Annotations panel of a green run.
//   - a JSON ledger under artifacts/test-ledger/, uploaded per job and aggregated by the
//     `flakiness` roll-up job in tests.yml.
//
// The roll-up is the one that matters most. Per-job counts answer "is this job flaky"; only the
// roll-up answers "which suites are costing us, and is it getting worse", which is the question
// that decides where the next fix goes.
//
// Deliberately NOT here: failing the build past a retry threshold. A suite legitimately sitting at
// three retries today would be one bad day away from a red master, and the value is in visibility,
// not a cliff.
partial class Build
{
    /// <summary>
    /// Per-project ledger files, one per project+framework, uploaded as a CI artifact. Under
    /// artifacts/ because <see cref="Clean"/> empties it at the start of every run, so a ledger can
    /// never be a stale leftover from a previous invocation.
    /// </summary>
    static AbsolutePath LedgerDirectory => RootDirectory / "artifacts" / "test-ledger";

    /// <summary>
    /// The CI job this run belongs to (e.g. TestCore-net9.0-Newtonsoft). Set by tests.yml from the
    /// matrix entry: the Nuke target name isn't reachable from here, and GITHUB_JOB reports the
    /// matrix's job id, which is the same string for every job in the matrix.
    /// </summary>
    static string ledgerJobName => Environment.GetEnvironmentVariable("CI_JOB_NAME") is { Length: > 0 } name
        ? name
        : "local";

    void recordLedger(string projectName, string framework, SupervisorResults results)
    {
        var entry = new LedgerEntry
        {
            Job = ledgerJobName,
            Project = projectName,
            Framework = framework,
            Tests = results.Tests.Count,
            CleanPasses = results.CleanPasses.Count,
            PassedOnRetry = results.PassedOnRetry.Count,
            RetriesPerformed = results.RetriesPerformed,
            Failed = results.Failed.Count,
            Indeterminate = results.Indeterminate.Count,
            WorkerFaults = results.WorkerFaults.Count,
            AbortReason = results.AbortReason,
            // Bounded by the budget itself: the point of the list is to name the suspects, not to
            // reproduce the log.
            FlakyTests = results.PassedOnRetry.Select(x => x.DisplayName).Take(MaxRetriesPerRun).ToArray(),
            FlakyFailures = results.PassedOnRetry.Take(MaxRetriesPerRun).Select(firstFailure).ToArray()
        };

        writeLedgerFile(entry);
        appendStepSummary(entry);
        annotate(entry);
    }

    /// <summary>
    /// Why a retried test failed the first time.
    ///
    /// <para>A name alone is a lead you cannot follow: once a test passes on attempt two, its first
    /// attempt's failure appears nowhere at all — not in the supervisor's log, which keeps only the
    /// <c>[FLAKY] ... passed on attempt 2</c> line, and not in any TRX, because the run as a whole
    /// succeeded. Capturing the first failure is the difference between "this one is flaky" and
    /// "this one times out waiting on the daemon's high-water mark".</para>
    ///
    /// <para>Takes the earliest attempt that did not succeed rather than <c>Attempts[0]</c>: the
    /// ordering of that collection is Bobcat's business, and <c>AttemptNumber</c> is the field that
    /// actually states it.</para>
    ///
    /// <para>The reason comes from the attempt's <c>Outcome</c> — the test's own error — and
    /// deliberately <b>not</b> from <c>Disposition.Reason</c>, which is the supervisor's rationale
    /// for retrying and is the same sentence for every retry in the repository.</para>
    /// </summary>
    static FlakyFailure firstFailure(TestReport report)
    {
        var failed = report.Attempts?
            .Where(x => !x.Succeeded)
            .OrderBy(x => x.AttemptNumber)
            .FirstOrDefault();

        var outcome = failed?.Outcome;

        return new FlakyFailure
        {
            Test = report.DisplayName,
            Attempt = failed?.AttemptNumber ?? 0,
            ErrorType = string.IsNullOrWhiteSpace(outcome?.ErrorType) ? null : outcome.ErrorType,
            // One line and bounded: this is a lead to open the log with, not a copy of it. A
            // multi-line reason would also break the ::warning workflow command downstream.
            Reason = condense(outcome?.ErrorMessage),
            Stack = topFrames(outcome?.StackTrace)
        };
    }

    /// <summary>
    /// The top frames of the failing attempt's stack, for the ledger JSON only. The message says
    /// what went wrong; the stack says where, and for a whole class of flake — a 23505 raised from
    /// somewhere inside an append, say — the call site is the only thing that separates the
    /// candidates. Kept off the step summary and the annotation, which are glanceable surfaces a
    /// stack would drown; the JSON artifact is the right place for something you go looking for.
    /// </summary>
    static string[] topFrames(string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace)) return [];

        // Enough to cross a teardown/dispatch boundary and name the caller, not so much that the
        // ledger becomes a copy of the log.
        const int maxFrames = 12;

        return stackTrace
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Take(maxFrames)
            .ToArray();
    }

    /// <summary>
    /// Collapses a failure reason to a single bounded line, or null when there is nothing to say.
    /// </summary>
    static string condense(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var flattened = string.Join(" ", reason.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0));

        const int limit = 400;
        return flattened.Length <= limit ? flattened : flattened[..limit] + "…";
    }

    static void writeLedgerFile(LedgerEntry entry)
    {
        try
        {
            LedgerDirectory.CreateDirectory();

            // One file per project+framework: a target can run several projects, and a project can
            // run under several TFMs, so neither alone is a unique key.
            var path = LedgerDirectory / $"{entry.Job}.{entry.Project}.{entry.Framework}.json";

            File.WriteAllText(path, JsonSerializer.Serialize(entry, LedgerJson));
        }
        catch (Exception e)
        {
            // Reporting about the tests must never be what fails the tests.
            Log.Warning(e, "Could not write the retry ledger for {Project}", entry.Project);
        }
    }

    /// <summary>
    /// Appends this project's row to the job summary. Every supervised project in the job appends
    /// to the same file, so the header is written once, on the first append.
    /// </summary>
    static void appendStepSummary(LedgerEntry entry)
    {
        var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrEmpty(summaryFile)) return;

        try
        {
            var builder = new StringBuilder();

            if (new FileInfo(summaryFile) is { Exists: false } or { Length: 0 })
            {
                builder.AppendLine($"### Retry ledger — {entry.Job}");
                builder.AppendLine();
                builder.AppendLine("| project | tests | clean | passed on retry | retries | failed | indeterminate |");
                builder.AppendLine("|---|--:|--:|--:|--:|--:|--:|");
            }

            builder.AppendLine(
                $"| {entry.Project} ({entry.Framework}) | {entry.Tests} | {entry.CleanPasses} | " +
                $"{mark(entry.PassedOnRetry)} | {mark(entry.RetriesPerformed)} | {mark(entry.Failed)} | " +
                $"{mark(entry.Indeterminate)} |");

            if (entry.AbortReason is not null)
            {
                builder.AppendLine();
                builder.AppendLine($"> **ABORTED** — {entry.AbortReason}");
            }

            if (entry.FlakyTests.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine("<details><summary>Tests that only passed on a retry</summary>");
                builder.AppendLine();

                // Keyed off FlakyFailures where it has something to add, so the reason sits with the
                // name rather than in a log nobody opens.
                var reasons = entry.FlakyFailures.ToDictionary(x => x.Test, x => x);
                foreach (var test in entry.FlakyTests)
                {
                    builder.AppendLine($"- `{test}`");

                    if (!reasons.TryGetValue(test, out var failure)) continue;
                    if (failure.ErrorType is null && failure.Reason is null) continue;

                    var label = failure.ErrorType is null ? "" : $"**{failure.ErrorType}**";
                    var detail = failure.Reason is null ? "" : $" — {failure.Reason}";
                    builder.AppendLine($"  - attempt {failure.Attempt}: {label}{detail}");
                }

                builder.AppendLine();
                builder.AppendLine("</details>");
            }

            builder.AppendLine();

            File.AppendAllText(summaryFile, builder.ToString());
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not append to $GITHUB_STEP_SUMMARY for {Project}", entry.Project);
        }

        // Zero reads as zero; anything else is bolded, because the eye is meant to stop on it.
        static string mark(int count) => count == 0 ? "0" : $"**{count}**";
    }

    /// <summary>
    /// Emits the retry count as a GitHub annotation. Serilog warnings are not annotations, so this
    /// has to be the literal workflow command on stdout. Only fires when something was retried:
    /// an annotation on every green job is an annotation nobody reads.
    /// </summary>
    static void annotate(LedgerEntry entry)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;
        if (entry.RetriesPerformed == 0) return;

        var suspects = entry.FlakyTests.Length > 0
            ? $" First flaky test: {entry.FlakyTests[0]}."
            : "";

        // Workflow commands are newline-delimited, so the message has to be a single line.
        Console.WriteLine(
            $"::warning title={entry.Job}: {entry.RetriesPerformed} retries::" +
            $"{entry.Project} ({entry.Framework}) spent {entry.RetriesPerformed} of its " +
            $"{MaxRetriesPerRun}-retry budget; " +
            $"{entry.PassedOnRetry} test(s) passed only on a retry.{suspects}");
    }

    static readonly JsonSerializerOptions LedgerJson = new() { WriteIndented = true };

    /// <summary>
    /// One supervised project run, as the roll-up job consumes it. The property names are the
    /// contract with the `flakiness` job's jq in tests.yml — rename one and that jq goes silently
    /// null rather than failing.
    /// </summary>
    class LedgerEntry
    {
        public string Job { get; init; }
        public string Project { get; init; }
        public string Framework { get; init; }
        public int Tests { get; init; }
        public int CleanPasses { get; init; }
        public int PassedOnRetry { get; init; }
        public int RetriesPerformed { get; init; }
        public int Failed { get; init; }
        public int Indeterminate { get; init; }
        public int WorkerFaults { get; init; }
        public string AbortReason { get; init; }
        public string[] FlakyTests { get; init; } = [];

        /// <summary>
        /// The same tests as <see cref="FlakyTests"/>, each with the reason its first attempt
        /// failed. Additive on purpose: <see cref="FlakyTests"/> stays a plain string array so the
        /// roll-up's `map(.FlakyTests[])` keeps working against ledgers written by older runs.
        /// </summary>
        public FlakyFailure[] FlakyFailures { get; init; } = [];
    }

    /// <summary>
    /// One retried test and why it failed before it passed. See <see cref="firstFailure"/>.
    /// </summary>
    class FlakyFailure
    {
        public string Test { get; init; }
        public int Attempt { get; init; }

        /// <summary>Exception type name from the failing attempt, e.g. ShouldAssertException.</summary>
        public string ErrorType { get; init; }

        /// <summary>The failing attempt's own error message, flattened to one bounded line.</summary>
        public string Reason { get; init; }

        /// <summary>
        /// Top frames of the failing attempt's stack. Ledger JSON only — see <see cref="topFrames"/>
        /// for why it is not rendered on the summary surfaces.
        /// </summary>
        public string[] Stack { get; init; } = [];
    }
}
