using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bobcat.Resilience;
using Bobcat.Supervisor;
using Npgsql;
using Nuke.Common;
using Nuke.Common.IO;
using Serilog;

// The supervised test runner (#5096, extended for JasperFx/bobcat#565): every test target drives
// its project's Microsoft.Testing.Platform host through Bobcat's Supervisor
// (https://github.com/JasperFx/bobcat) instead of shelling out to `dotnet test`.
//
// What that buys, and why it is here:
//
//   - Worker processes, partitioned by test CLASS. A suite with `workers > 1` is split across that
//     many test hosts, each pointed at its own Postgres database (see "Per-lane databases" below).
//     Partitioning is by class and never by test: a class is the unit xUnit's isolation contract
//     (fixtures, static state) is written against, and splitting one was measured on Wolverine as
//     1-4 non-deterministic failures.
//
//   - Retries are OPT-IN, and always in a FRESH process. A test is retried only when it carries
//     [Trait("Retry", "N")] (or [Trait("Isolated", "true")]); everything else that fails, fails.
//     Since Bobcat 1.0 (bobcat#661) that is Bobcat's DefaultFailurePolicy as it ships: a Retry trait
//     retries in a fresh process unless the test also says [Trait("RetryWarm", "true")], which
//     nothing in Marten does. The repo's own fresh-process policy from bobcat#565 is gone.
//     Up to bobcat#565 every failure here was retried, which made "flaky" and "broken" impossible
//     to tell apart without opening the ledger. A test earns a Retry trait with evidence: the
//     flakiness roll-up names every test that only passed on a retry. A retry runs in a fresh
//     process because a failed Marten test usually leaves a half-built store, schema objects or a
//     running daemon behind — the fresh process IS the reset bracket.
//
//   - Honest reporting. A pass-on-retry is never folded into a clean pass (RetryLedger.cs); a
//     crashed worker is Indeterminate, with its exit code and stderr; a test in flight past five
//     minutes is named as STALLED (report-only); Bobcat's own RunReport lands in the job summary.
//
//   - Lane balancing from measured durations. The previous master run's per-project TestLedger,
//     fetched as a CI artifact, feeds Supervisor.KnownTestDurations, so lanes are balanced
//     longest-processing-time-first instead of by test count (TestLedger.cs).
//
// Parallelism is still mainly at the JOB level: .github/workflows/tests.yml gives every project its
// own runner and its own Postgres. Workers > 1 is reserved for the suites that set the workflow's
// wall clock, and only after the suite was checked against the bug shapes in
// https://github.com/JasperFx/bobcat/blob/main/docs/parallel-ready-suites.md.
//
// The executable this drives comes from <UseMicrosoftTestingPlatformRunner> in src/Tests.props.
partial class Build
{
    /// <summary>
    /// Overrides every target's worker count (<c>--test-workers 4</c>), bypassing the per-machine
    /// clamp. For measuring; the committed per-target counts are the tuned values.
    /// </summary>
    [Parameter] readonly int? TestWorkers;

    /// <summary>
    /// Turns off retries entirely (<c>--disable-test-retry</c>), including those a test opted into
    /// with a Retry trait. Measure a suite's real stability with this on.
    /// </summary>
    [Parameter] readonly bool DisableTestRetry;

    /// <summary>
    /// Retries a whole project run may spend before every remaining failure is reported as failed
    /// WITHOUT being retried at all. Read the retry ledger against this number: a run sitting at
    /// the cap is not "N flaky tests", it is N flaky tests plus an unknown tail.
    /// </summary>
    const int MaxRetriesPerRun = 25;

    /// <summary>
    /// The run-wide ceiling on attempts for any one test, including the first. A test's own
    /// <c>[Trait("Retry", "N")]</c> can LOWER this but never raise it (Bobcat's
    /// <c>RetryBudget.AttemptsAllowedFor</c>): the operator owns how much time a run may spend.
    /// Three is the ceiling the pre-#565 harness gave every test, kept for the tests that opt in.
    /// </summary>
    const int MaxAttemptsPerTest = 3;

    /// <summary>
    /// A test in flight this long is named in the log, the job summary and the run report. Report
    /// only: nothing is killed. Five minutes is Wolverine's calibration, and nothing in Marten's
    /// suites legitimately runs that long as ONE test.
    /// </summary>
    static readonly TimeSpan StallThreshold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Runs one test project through the supervisor, once per target framework.
    /// </summary>
    /// <param name="projectPath">Repo-relative path to the .csproj.</param>
    /// <param name="frameworkOverride">
    /// Pins the run to one TFM regardless of <c>--framework</c>. Only for a project whose suite
    /// is meaningful on a single framework.
    /// </param>
    /// <param name="workers">
    /// Worker processes for the suite. 1 (sequential, one database) unless the suite has been
    /// profiled AND checked for the parallel-ready bug shapes. With more than one, every lane gets
    /// its own database (<see cref="laneEnvironment"/>). Clamped to <c>ProcessorCount / 2</c> on
    /// the machine running unless <c>--test-workers</c> says otherwise.
    /// </param>
    void RunTestProject(string projectPath, string frameworkOverride = null, int workers = 1)
    {
        var failed = new List<string>();

        foreach (var framework in frameworksBuiltFor(projectPath, frameworkOverride))
        {
            if (!runSupervised(projectPath, framework, workers)) failed.Add($"{projectPath} ({framework})");
        }

        if (failed.Count > 0) throw new InvalidOperationException($"Tests failed: {string.Join(", ", failed)}");
    }

    bool runSupervised(string projectPath, string framework, int workers)
    {
        workers = workerCountFor(workers);

        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var executable = testHostFor(projectPath, framework);

        Log.Information("=== {Project} ({Framework}): supervised run, {Workers} worker(s){Retry} ===",
            projectName, framework, workers, DisableTestRetry ? ", retries OFF" : "");

        var factory = new MtpWorkerFactory(executable)
        {
            EnvironmentFor = workers > 1 ? laneEnvironment(workers) : null
        };

        var ledger = loadTestLedger(projectName, framework);

        var supervisor = new Supervisor(factory)
        {
            MaxParallelWorkers = workers,
            RetryBudget = DisableTestRetry
                ? RetryBudget.None
                : new RetryBudget { MaxAttemptsPerTest = MaxAttemptsPerTest, MaxRetriesPerRun = MaxRetriesPerRun },

            // The policy below only ever retries in a fresh process, so idle lane workers are
            // released before the retry passes. Without it, workers+1 test hosts sit resident at
            // once — which is what OOM-killed 16GB GitHub runners on Wolverine, both times during a
            // retry.
            ReleaseIdleLanes = true,

            // Report-only observability. A test in flight past the threshold is named at once (a
            // wedged daemon test used to be visible only as a job hitting its 20 minute cap), and a
            // one-line heartbeat every 30s says how far a run got and what has been running longest.
            StallThreshold = StallThreshold,
            StallAction = StallAction.Report,
            HeartbeatInterval = TimeSpan.FromSeconds(30),

            // Longest-processing-time-first lane balancing from the previous run's ledger, keyed by
            // display name (bobcat#730). No ledger, or no match, degrades to balancing by test
            // count, and the run report's first lines say how many durations matched.
            KnownTestDurations = ledger.KnownDurations(),

            Log = message => Log.Information("  {Message}", message)
        };

        var results = supervisor.Run().GetAwaiter().GetResult();

        // Said after the fact because the match can only be made once discovery has run. Without
        // this line a ledger that matches nothing is indistinguishable from one that works.
        if (workers > 1 && results.DurationCoverage is { } coverage)
            Log.Information("  lanes were balanced on the previous run's ledger: {Coverage}", coverage);

        recordTestLedger(ledger, projectName, framework, results);
        writeRunReport(projectName, framework, results);

        return report(projectName, framework, results);
    }

    /// <summary>
    /// The committed worker count is tuned on a developer machine; the machine running decides
    /// what it can carry. A GitHub-hosted runner has 4 vCPUs and 16GB, and four Marten test hosts
    /// beside Postgres got a Wolverine runner killed with a shutdown signal — an oversubscribed
    /// fleet does not fail politely. An explicit <c>--test-workers</c> bypasses the clamp.
    /// </summary>
    int workerCountFor(int committed)
    {
        if (TestWorkers is { } asked) return Math.Max(1, asked);

        var ceiling = Math.Max(1, Environment.ProcessorCount / 2);
        if (committed <= ceiling) return committed;

        Log.Information("Clamping {Asked} workers to {Ceiling} for this machine's {Cores} core(s)",
            committed, ceiling, Environment.ProcessorCount);
        return ceiling;
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

        // Report-only, like everything the stall threshold does: a test that ran past it and then
        // passed is still worth a line, because a green run is where a creeping slowdown hides.
        foreach (var stalled in results.StalledTests)
            Log.Warning("  [STALLED] {Test} — {Seconds}s in flight on lane {Lane}",
                stalled.DisplayName, (int)stalled.InFlight.TotalSeconds, stalled.Worker.Lane);

        foreach (var test in results.Indeterminate)
            Log.Error("  [INDETERMINATE] {Test} — {Error}", test.DisplayName, test.Final.Outcome.ErrorMessage);

        foreach (var test in results.Failed.Where(x => !x.IsIndeterminate))
            Log.Error("  [FAILED] {Test} — {Error}", test.DisplayName, test.Final.Outcome.ErrorMessage);

        return results.ExitCode == 0;
    }

    // ─── Per-lane databases ────────────────────────────────────────────

    /// <summary>
    /// The connection string the suites read when nothing overrides it — the same default as
    /// src/Marten.Testing/Harness/ConnectionSource.cs, so a lane differs from a sequential run in
    /// its database name and nothing else.
    /// </summary>
    const string DefaultTestConnectionString =
        "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5";

    static string baseTestConnectionString =>
        Environment.GetEnvironmentVariable("marten_testing_database") is { Length: > 0 } configured
            ? configured
            : DefaultTestConnectionString;

    /// <summary>
    /// Points each worker at its own database through <c>marten_testing_database</c>, the variable
    /// ConnectionSource already reads. Marten's suites hard-code their schema names, so two
    /// processes sharing one database collide however the classes are partitioned: isolation has to
    /// be the catalog. Discovery, isolated and recycled workers all report lane 0, so the databases
    /// provisioned equal the workers asked for, not the processes launched.
    /// </summary>
    /// <remarks>
    /// The variable carries a whole connection string built with NpgsqlConnectionStringBuilder
    /// rather than a database name stitched onto a base string: a textual replace that stops
    /// matching once the base changes quietly points every lane at one database.
    /// </remarks>
    Func<WorkerLaunchContext, IReadOnlyDictionary<string, string>> laneEnvironment(int workers)
    {
        var lanes = Enumerable.Range(0, workers).Select(laneConnectionString).ToArray();
        ensureLaneDatabases(lanes);

        return context => new Dictionary<string, string>
        {
            ["marten_testing_database"] = lanes[Math.Clamp(context.Lane, 0, lanes.Length - 1)]
        };
    }

    static string laneConnectionString(int lane)
    {
        var builder = new NpgsqlConnectionStringBuilder(baseTestConnectionString);
        builder.Database = $"{builder.Database}_w{lane}";
        return builder.ConnectionString;
    }

    static void ensureLaneDatabases(IEnumerable<string> lanes)
    {
        // CREATE DATABASE cannot run inside a transaction or on a multiplexed connection, hence a
        // dedicated unpooled admin connection to the base database.
        var admin = new NpgsqlConnectionStringBuilder(baseTestConnectionString) { Pooling = false };
        using var conn = new NpgsqlConnection(admin.ConnectionString);
        conn.Open();

        foreach (var lane in lanes)
        {
            var database = new NpgsqlConnectionStringBuilder(lane).Database!;

            using var check = conn.CreateCommand();
            check.CommandText = "select 1 from pg_database where datname = @name";
            check.Parameters.AddWithValue("name", database);
            if (check.ExecuteScalar() is not null) continue;

            Log.Information("Provisioning per-lane database {Database}", database);
            using var create = conn.CreateCommand();
            // An identifier, not a value, so it cannot be parameterized; it is generated above from
            // the configured database name, never user input.
            create.CommandText = $"CREATE DATABASE \"{database}\"";
            create.ExecuteNonQuery();
        }
    }

    // ─── Bobcat's run report ───────────────────────────────────────────

    /// <summary>
    /// Bobcat's own rendering of the run: the text view (timing, slowest tests, stalls, quarantine)
    /// collapsed into the job summary, and the JSON view beside the retry ledger for the artifact.
    /// Best-effort, like every other report here.
    /// </summary>
    static void writeRunReport(string projectName, string framework, SupervisorResults results)
    {
        try
        {
            // A subdirectory, not beside the retry ledgers: the flakiness roll-up in tests.yml reads
            // every top-level *.json as a retry ledger and asserts on its schema.
            var reports = LedgerDirectory / "run-reports";
            reports.CreateDirectory();
            File.WriteAllText(
                reports / (fileNameSafe($"{ledgerJobName}.{projectName}.{framework}") + ".json"),
                RunReport.ToJson(results));

            var summaryFile = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
            if (string.IsNullOrEmpty(summaryFile)) return;

            File.AppendAllText(summaryFile,
                $"<details><summary>Run report — {projectName} ({framework})</summary>{Environment.NewLine}{Environment.NewLine}" +
                $"```{Environment.NewLine}{RunReport.ToText(results)}{Environment.NewLine}```{Environment.NewLine}{Environment.NewLine}" +
                $"</details>{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not write the run report for {Project}", projectName);
        }
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
