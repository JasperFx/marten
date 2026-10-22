using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bobcat.Ledger;
using Bobcat.Supervisor;
using Npgsql;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

// The local commit-and-push gate (#5725): the logical suites of .github/workflows/tests.yml, run on
// this machine through Bobcat.Supervisor's Gate (bobcat#729,
// https://github.com/JasperFx/bobcat/blob/main/docs/local-gates.md) instead of one runner per job.
//
//   ./build.sh LocalGate
//
// What it does, in order:
//
//   1. Init ONCE (Clean, connection.txt, AssemblyInfo.cs) and preflight the Postgres server. CI runs
//      `compile-project` per job, and that target's Init → Clean() wipes artifacts/test-ledger and
//      rewrites AssemblyInfo.cs in eight projects; run per unit, it does both under the feet of every
//      other unit in flight.
//   2. Build src/Marten.slnx -c Release ONCE (both target frameworks), then every test host runs as
//      built.
//   3. Run each test project as a unit, on its newest target framework unless `--framework all`,
//      longest first, at most `--gate-worker-budget` worker processes at once across all units. A
//      unit runs the same supervisor configuration its CI target does (configureSupervisor): the
//      opt-in [Trait("Retry", "N")] policy, the stall report, lane balancing from the ledger.
//   4. Report one line per unit, then the full run report of every unit that was not clean.
//
// Databases. Every suite defaults to `marten_testing` on 5432 and hard-codes its schema names, so two
// units on one database collide however their classes are split. Each unit therefore gets its own
// databases through `marten_testing_database` (which ConnectionSource reads), one per lane:
// `marten_testing_<project>_<tfm>_w{n}` — never the `marten_testing_w{n}` a CI target's lanes use,
// and never anything another repository's lanes (Weasel's) are named.
//
// What a unit's own database cannot isolate is a name a suite reaches PAST its lane for: a whole
// database with a fixed name on the server, or the default database itself. Those are the tracks
// below. A track is exclusive, so the gate never runs two units that share fixed names at once.
partial class Build
{
    /// <summary>
    /// LocalGate: the Postgres server (and base database) every unit's lane databases are created on.
    /// Defaults to <c>marten_testing_database</c>, else the ordinary localhost:5432/marten_testing.
    /// Point it at a Postgres 15 container for the PG15 leg (see CLAUDE.md).
    /// </summary>
    [Parameter("LocalGate: connection string of the Postgres server the units' databases are created on")]
    readonly string GateDatabase;

    /// <summary>
    /// LocalGate: DEFAULT_SERIALIZER for every unit — <c>Newtonsoft</c>, <c>SystemTextJson</c>, or
    /// <c>split</c> for CI's pairing (net9.0 on Newtonsoft, net10.0 on System.Text.Json). Unset
    /// leaves the suites on their own default (Newtonsoft) or whatever the shell exported.
    /// </summary>
    [Parameter("LocalGate: DEFAULT_SERIALIZER for every unit: Newtonsoft, SystemTextJson, or split (CI's net9/net10 pairing)")]
    readonly string GateSerializer;

    /// <summary>
    /// LocalGate: opt-in suites that need something beyond the ordinary database — <c>postgis</c>,
    /// <c>timescaledb</c>, <c>multihost</c>, <c>stress</c>.
    /// </summary>
    [Parameter("LocalGate: opt-in suites: postgis, timescaledb, multihost, stress")]
    readonly string[] GateInclude = [];

    /// <summary>LocalGate: run only these suites (by name, e.g. <c>Linq</c>, or project, e.g. <c>LinqTests</c>).</summary>
    [Parameter("LocalGate: run only these suites, by name (Linq) or project (LinqTests)")]
    readonly string[] GateOnly = [];

    /// <summary>LocalGate: worker processes across every unit at once. Defaults to half the cores.</summary>
    [Parameter("LocalGate: worker processes across every unit at once (default: half the cores)")]
    readonly int? GateWorkerBudget;

    /// <summary>LocalGate: skip the solution build; every host must already be built in Release.</summary>
    [Parameter("LocalGate: skip the solution build, the Release hosts are already there")]
    readonly bool GateSkipBuild;

    /// <summary>
    /// LocalGate: the TimescaleDB server for <c>--gate-include timescaledb</c>. Defaults to
    /// docker-compose.timescaledb.yml's port, 5433 — which is also Wolverine's Postgres, so the
    /// preflight checks the extension is really there.
    /// </summary>
    [Parameter("LocalGate: TimescaleDB server for --gate-include timescaledb (default localhost:5433)")]
    readonly string GateTimescaleDatabase;

    // ─── Tracks ────────────────────────────────────────────────────────

    /// <summary>
    /// Whole databases named tenant1..tenant6 and database1 on the server: CoreTests
    /// (jasper_fx_mechanics), DaemonTests (multi_tenancy_by_database, dynamic_spin_up_of_dynamic_tenants),
    /// MultiTenancyTests throughout, and StressTests.
    /// </summary>
    const string TenantDatabasesTrack = "tenant-databases";

    /// <summary>Whole databases named marten_shard_*: MultiTenancyTests and TenantPartitionedEventsTests.</summary>
    const string ShardDatabasesTrack = "shard-databases";

    /// <summary>
    /// The default database itself, reached past the lane: EventSourcingTests' testing_projections
    /// hard-codes localhost:5432/marten_testing. Every other unit is relocated, so this only keeps two
    /// frameworks of EventSourcingTests apart, and StressTests away from it.
    /// </summary>
    const string DefaultDatabaseTrack = "default-database";

    const string PgVectorTrack = "pgvector";
    const string PostGisTrack = "postgis";
    const string TimescaleTrack = "timescaledb";
    const string MultiHostTrack = "multihost";

    /// <summary>MultiHostTests' hard-coded primary/standby pair (src/MultiHostTests/docker-compose.yaml).</summary>
    static readonly (string Host, int Port)[] MultiHostPair = [("localhost", 5440), ("localhost", 5441)];

    const string DefaultTimescaleConnectionString =
        "Host=localhost;Port=5433;Database=marten_testing;Username=postgres;password=postgres;Command Timeout=5";

    /// <summary>One logical suite of tests.yml, as the gate runs it.</summary>
    /// <param name="Name">The short name the matrix uses (and <c>--gate-only</c> takes).</param>
    /// <param name="Project">Repo-relative .csproj.</param>
    /// <param name="Workers">The worker count its CI target commits to (build.cs).</param>
    /// <param name="Tracks">Fixed server-level names it holds while it runs.</param>
    /// <param name="OptIn">The <c>--gate-include</c> name that turns it on; null runs by default.</param>
    /// <param name="Server">The server its lanes go on; null is the gate's database.</param>
    /// <param name="Relocate">
    /// False for a suite that gets no lane databases: one with no database at all (SourceGenerator),
    /// or one whose connection string is hard-coded (MultiHostTests).
    /// </param>
    sealed record GateSuite(
        string Name,
        string Project,
        int Workers = 1,
        string[] Tracks = null,
        string OptIn = null,
        string Server = null,
        bool Relocate = true)
    {
        public string[] Tracks { get; init; } = Tracks ?? [];
    }

    /// <summary>
    /// The suites, longest first by the CI job times of master run 39327486906 — the order a first
    /// run (no .bobcat/gate-durations.json yet) offers them to the budget in. After that the gate
    /// orders by its own record of this machine. The matrix's AOT runtime job is not here: it
    /// publishes a native binary rather than running a test host (<c>./build.sh test-aot-runtime</c>).
    /// </summary>
    IReadOnlyList<GateSuite> gateSuites() =>
    [
        new("Daemon", "src/DaemonTests/DaemonTests.csproj", Tracks: [TenantDatabasesTrack]),
        new("EventSourcing", "src/EventSourcingTests/EventSourcingTests.csproj", Workers: 4, Tracks: [DefaultDatabaseTrack]),
        new("Linq", "src/LinqTests/LinqTests.csproj", Workers: 4),
        new("TenantPartitionedEvents", "src/TenantPartitionedEventsTests/TenantPartitionedEventsTests.csproj", Tracks: [ShardDatabasesTrack]),
        new("DocumentDb", "src/DocumentDbTests/DocumentDbTests.csproj"),
        new("Core", "src/CoreTests/CoreTests.csproj", Tracks: [TenantDatabasesTrack]),
        new("ValueTypes", "src/ValueTypeTests/ValueTypeTests.csproj"),
        new("MultiTenancy", "src/MultiTenancyTests/MultiTenancyTests.csproj", Tracks: [TenantDatabasesTrack, ShardDatabasesTrack]),
        new("EntityFrameworkCore", "src/Marten.EntityFrameworkCore.Tests/Marten.EntityFrameworkCore.Tests.csproj"),
        new("ModularConfig", "src/ModularConfigTests/ModularConfigTests.csproj"),
        new("Patching", "src/PatchingTests/PatchingTests.csproj"),
        new("ContainerScopedProjections", "src/ContainerScopedProjectionTests/ContainerScopedProjectionTests.csproj"),
        new("BaseLib", "src/Marten.Testing/Marten.Testing.csproj"),
        new("CompiledQueries", "src/CompiledQueryTests/CompiledQueryTests.csproj"),
        // docker-compose.yml's image layers pgvector (and PostGIS) onto postgres:17; the preflight
        // says so when the server lacks it.
        new("PgVector", "src/Marten.PgVector.Tests/Marten.PgVector.Tests.csproj", Tracks: [PgVectorTrack]),
        // Pure Roslyn: no database, so no lanes to provision.
        new("SourceGenerator", "src/Marten.SourceGenerator.Tests/Marten.SourceGenerator.Tests.csproj", Relocate: false),
        new("MemoryPack", "src/Marten.MemoryPack.Tests/Marten.MemoryPack.Tests.csproj"),
        new("AspNetCore", "src/Marten.AspNetCore.Testing/Marten.AspNetCore.Testing.csproj"),
        new("NodaTime", "src/Marten.NodaTime.Testing/Marten.NodaTime.Testing.csproj"),

        // ─── Opt-in: --gate-include <name> ─────────────────────────────
        new("PostGIS", "src/Marten.PostGIS.Tests/Marten.PostGIS.Tests.csproj", Tracks: [PostGisTrack], OptIn: "postgis"),
        new("TimescaleDB", "src/Marten.TimescaleDB.Tests/Marten.TimescaleDB.Tests.csproj", Tracks: [TimescaleTrack],
            OptIn: "timescaledb", Server: GateTimescaleDatabase ?? DefaultTimescaleConnectionString),
        new("MultiHost", "src/MultiHostTests/MultiHostTests.csproj", Tracks: [MultiHostTrack], OptIn: "multihost", Relocate: false),
        // Not in tests.yml (see TestStress in build.cs). It creates and drops whole databases, so it
        // holds every fixed-name track.
        new("Stress", "src/StressTests/StressTests.csproj",
            Tracks: [TenantDatabasesTrack, ShardDatabasesTrack, DefaultDatabaseTrack], OptIn: "stress"),
    ];

    /// <summary>Where the gate keeps its folded duration ledgers between runs: Clean() empties artifacts/.</summary>
    AbsolutePath GateLedgerDirectory => RootDirectory / ".bobcat" / "test-ledger";

    string gateBaseConnectionString => string.IsNullOrWhiteSpace(GateDatabase) ? baseTestConnectionString : GateDatabase;

    /// <summary>
    /// The full tests.yml matrix, locally, through Bobcat's Gate. See the top of this file and the
    /// "Local gate" section of CLAUDE.md.
    /// </summary>
    Target LocalGate => _ => _
        .DependsOn(Init)
        .Executes(async () =>
        {
            var suites = selectGateSuites();

            // Preflight the ordinary server before building anything, the way the gate preflights a
            // track: a stopped container is one line in seconds, not a thousand failures later.
            int? connectionBudget = null;
            if (suites.Any(s => s.Relocate && s.Server is null))
                connectionBudget = workersTheServerHolds(checkPostgres(gateBaseConnectionString, "the gate database"));

            var ledgers = new ConcurrentDictionary<string, TestLedger>(StringComparer.Ordinal);
            var gate = declareGate(suites, ledgers);
            gate.WorkerBudget = gateWorkerBudget(gate.WorkerBudget, connectionBudget);

            Log.Information("LocalGate: {Count} suite(s) on {Server}{Tag}", suites.Count,
                describeServer(gateBaseConnectionString),
                gate.RunTagPrefix is { } tag ? $", publishing under {tag}/<unit>" : "");

            var results = await gate.Run();

            foreach (var unit in results.Units.Where(u => u.Results is not null))
            {
                var project = unit.Unit.Project.Name;
                var framework = unit.Unit.Framework;

                recordLedger(project, framework, unit.Results);
                writeRunReport(project, framework, unit.Results);
                if (ledgers.TryGetValue(unit.Unit.Name, out var ledger))
                    recordTestLedger(ledger, project, framework, unit.Results, GateLedgerDirectory);
            }

            if (results.ExitCode != 0)
            {
                Assert.Fail($"LocalGate exited {results.ExitCode}: {results.Clean.Count} clean, " +
                            $"{results.Flaky.Count} passed on retry, {results.Failed.Count} failed, " +
                            $"{results.Indeterminate.Count} indeterminate, {results.NotRun.Count} not run" +
                            (results.AbortReason is { } reason ? $" — {reason}" : ""));
            }
        });

    List<GateSuite> selectGateSuites()
    {
        var all = gateSuites();

        var optIns = all.Where(s => s.OptIn is not null).Select(s => s.OptIn).ToArray();
        var unknown = GateInclude.Where(x => !optIns.Contains(x, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (unknown.Length > 0)
            throw new ArgumentException(
                $"--gate-include {string.Join(", ", unknown)}: not an opt-in suite. Known: {string.Join(", ", optIns)}.");

        var selected = all
            .Where(s => s.OptIn is null || GateInclude.Contains(s.OptIn, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (GateOnly.Length == 0) return selected;

        static bool matches(GateSuite suite, string name) =>
            suite.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileNameWithoutExtension(suite.Project).Equals(name, StringComparison.OrdinalIgnoreCase);

        var missing = GateOnly.Where(name => !all.Any(s => matches(s, name))).ToArray();
        if (missing.Length > 0)
            throw new ArgumentException(
                $"--gate-only {string.Join(", ", missing)}: no such suite. Known: {string.Join(", ", all.Select(s => s.Name))}.");

        // --gate-only names a suite outright, opt-in or not.
        return all.Where(s => GateOnly.Any(name => matches(s, name))).ToList();
    }

    GateDeclaration declareGate(IReadOnlyList<GateSuite> suites, ConcurrentDictionary<string, TestLedger> ledgers)
    {
        var gate = new GateDeclaration
        {
            Frameworks = FrameworkSelection.Parse(Framework),
            Configuration = "Release",
            Solution = RootDirectory / "src" / "Marten.slnx",
            SkipBuild = GateSkipBuild,
            DurationsPath = RootDirectory / ".bobcat" / "gate-durations.json",
            Log = message => Log.Information("{Message:l}", message),

            // One solution build covers both target frameworks. The gate's default build passes
            // -f per framework, which fails the netstandard2.0 source generator projects.
            Build = (request, _) =>
            {
                DotNetBuild(s => s
                    .SetProjectFile(request.Solution)
                    .SetConfiguration(request.Configuration)
                    .SetProcessEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1"));
                return Task.CompletedTask;
            }
        };

        gate.Tracks.Add(new Track(TenantDatabasesTrack));
        gate.Tracks.Add(new Track(ShardDatabasesTrack));
        gate.Tracks.Add(new Track(DefaultDatabaseTrack));
        gate.Tracks.Add(new Track(PgVectorTrack).Check("the gate database offers the pgvector extension",
            () => checkExtension(gateBaseConnectionString, "vector",
                "PgVector needs the image docker-compose.yml builds (pgvector + PostGIS on postgres:17)")));
        gate.Tracks.Add(new Track(PostGisTrack).Check("the gate database offers the PostGIS extension",
            () => checkExtension(gateBaseConnectionString, "postgis",
                "PostGIS needs the image docker-compose.yml builds, or a postgis/postgis container through --gate-database")));
        gate.Tracks.Add(new Track(TimescaleTrack).Check("the TimescaleDB server offers the timescaledb extension",
            () => checkExtension(GateTimescaleDatabase ?? DefaultTimescaleConnectionString, "timescaledb",
                "start docker-compose.timescaledb.yml (port 5433, which Wolverine's Postgres also takes) or pass --gate-timescale-database")));
        gate.Tracks.Add(multiHostTrack());

        foreach (var suite in suites)
        {
            var path = RootDirectory / suite.Project;
            var project = gate.Project(path, TestWorkers is { } asked ? Math.Max(1, asked) : suite.Workers)
                .Targeting(declaredFrameworks(path))
                .Supervise((unit, supervisor) =>
                {
                    var ledger = loadTestLedger(PreviousTestLedger ?? GateLedgerDirectory, unit.Project.Name, unit.Framework);
                    ledgers[unit.Name] = ledger;
                    configureSupervisor(supervisor, ledger);
                })
                .LaunchingWith(unit => workerFactoryFor(suite, unit));

            if (suite.Tracks.Length > 0) project.On(suite.Tracks);
        }

        return gate;
    }

    /// <summary>
    /// The unit's worker factory: lane databases on its server, plus DEFAULT_SERIALIZER when the
    /// gate was asked for one. Called when the unit starts, so its lane databases are created then.
    /// </summary>
    IWorkerFactory workerFactoryFor(GateSuite suite, GateUnit unit)
    {
        var shared = gateSerializerFor(unit.Framework) is { } serializer
            ? new Dictionary<string, string> { ["DEFAULT_SERIALIZER"] = serializer }
            : null;

        if (!suite.Relocate) return new MtpWorkerFactory(unit.Executable, shared);

        var server = suite.Server ?? gateBaseConnectionString;
        var lanes = Enumerable.Range(0, Math.Max(1, unit.Workers))
            .Select(lane => gateLaneConnectionString(server, unit, lane))
            .ToArray();
        ensureLaneDatabases(server, lanes);

        return new MtpWorkerFactory(unit.Executable, shared) { EnvironmentFor = laneEnvironment(lanes) };
    }

    /// <summary>
    /// <c>{base database}_{project}_{tfm}_w{lane}</c>, lower-cased — e.g.
    /// <c>marten_testing_eventsourcingtests_net10_0_w3</c>. The unit's name keeps two frameworks of
    /// one project apart; the base database's name keeps a gate's lanes off a CI target's
    /// (<c>marten_testing_w{n}</c>).
    /// </summary>
    static string gateLaneConnectionString(string server, GateUnit unit, int lane)
    {
        var builder = new NpgsqlConnectionStringBuilder(server);
        var slug = new string(unit.Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var database = $"{builder.Database}_{slug}_w{lane}";

        // Postgres truncates an identifier past NAMEDATALEN - 1 (63 by default) and two long
        // names could then land on one database. Refuse instead.
        if (database.Length > 63)
            throw new InvalidOperationException($"Lane database name {database} is longer than Postgres' 63 characters.");

        builder.Database = database;
        return builder.ConnectionString;
    }

    string gateSerializerFor(string framework)
    {
        if (string.IsNullOrWhiteSpace(GateSerializer)) return null;

        return GateSerializer.Trim().ToLowerInvariant() switch
        {
            "split" => framework == "net9.0" ? "Newtonsoft" : "SystemTextJson",
            "newtonsoft" => "Newtonsoft",
            "systemtextjson" or "stj" => "SystemTextJson",
            _ => throw new ArgumentException(
                $"--gate-serializer {GateSerializer}: expected Newtonsoft, SystemTextJson or split.")
        };
    }

    /// <summary>
    /// What the project file declares, else the repository default in Directory.Build.props: most
    /// test projects inherit their frameworks, which the gate cannot read from the .csproj alone.
    /// </summary>
    string[] declaredFrameworks(AbsolutePath project)
    {
        var declared = TargetFrameworks.Declared(project);
        if (declared.Count == 0) declared = TargetFrameworks.Declared(RootDirectory / "Directory.Build.props");
        if (declared.Count == 0)
            throw new InvalidOperationException($"Neither {project} nor Directory.Build.props declares a target framework.");

        return declared.ToArray();
    }

    // ─── The worker budget ─────────────────────────────────────────────

    /// <summary>
    /// Connections to allow per worker process when sizing the budget from the server. Measured on
    /// this repo's suites at net10.0, one sample a second of pg_stat_activity: single lanes peaked at
    /// 76 (DocumentDbTests), 49 (an EventSourcingTests lane), 34 (a LinqTests lane) and 28
    /// (DaemonTests); the whole gate at a budget of 9 peaked at 143 together; and at a budget of 4 on
    /// a 100-connection server, EventSourcingTests' four lanes ran out on their own. Every
    /// DocumentStore a test builds has its own Npgsql pool, and an idle pooled connection lives for
    /// five minutes, so the count follows how many stores a suite builds rather than how busy it is.
    /// </summary>
    const int ConnectionsPerWorker = 40;

    /// <summary>Connections left for psql, the lane provisioning and anything else on the server.</summary>
    const int ConnectionHeadroom = 10;

    /// <summary>
    /// How many workers the server's free connections hold. A stock postgres image allows 100, which
    /// four EventSourcingTests lanes exceed on their own (53300: sorry, too many clients already) — a
    /// GitHub job never sees it because each job has its own server. docker-compose.yml raises it.
    /// </summary>
    static int workersTheServerHolds((int MaxConnections, int InUse) server)
        => Math.Max(1, (server.MaxConnections - server.InUse - ConnectionHeadroom) / ConnectionsPerWorker);

    /// <summary>
    /// <c>--gate-worker-budget</c> when given; otherwise the gate's default (half the cores), lowered
    /// to what the server's connections hold, and said so.
    /// </summary>
    int gateWorkerBudget(int cores, int? connections)
    {
        if (GateWorkerBudget is { } asked) return Math.Max(1, asked);
        if (connections is not { } held || held >= cores) return cores;

        Log.Warning(
            "LocalGate: the gate database's free connections hold about {Held} worker(s) at {PerWorker} each, " +
            "so the budget is {Held} rather than {Cores}. Raise max_connections (docker-compose.yml sets 500; " +
            "recreate the container) or pass --gate-worker-budget to override.",
            held, ConnectionsPerWorker, held, cores);
        return held;
    }

    // ─── Preflight ─────────────────────────────────────────────────────

    static string describeServer(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        return $"{builder.Host}:{builder.Port}/{builder.Database}";
    }

    static NpgsqlConnection openUnpooled(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, Timeout = 5 };
        var conn = new NpgsqlConnection(builder.ConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>Opens the server and returns its max_connections and the client connections already on it.</summary>
    static (int MaxConnections, int InUse) checkPostgres(string connectionString, string what)
    {
        try
        {
            using var conn = openUnpooled(connectionString);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "select current_setting('server_version'), current_setting('max_connections')::int, " +
                              "(select count(*)::int from pg_stat_activity where backend_type = 'client backend')";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var server = (MaxConnections: reader.GetInt32(1), InUse: reader.GetInt32(2));
            Log.Information(
                "Preflight: {What} at {Server} answers — Postgres {Version}, max_connections {Max}, {InUse} in use",
                what, describeServer(connectionString), reader.GetString(0), server.MaxConnections, server.InUse);
            return server;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Preflight: nothing usable at {describeServer(connectionString)} for {what} ({e.Message}). " +
                "Start it with `docker compose up -d`; nothing was built or run.", e);
        }
    }

    static void checkExtension(string connectionString, string extension, string remedy)
    {
        using var conn = openUnpooled(connectionString);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "select count(*) from pg_available_extensions where name = @name";
        cmd.Parameters.AddWithValue("name", extension);
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
            throw new InvalidOperationException(
                $"{describeServer(connectionString)} has no '{extension}' extension: {remedy}.");
    }

    /// <summary>
    /// MultiHostTests hard-codes its pair and its credentials, so "something listens on 5440" is not
    /// enough: another container can hold the port. The check logs in as the suite will.
    /// </summary>
    static Track multiHostTrack()
    {
        var track = new Track(MultiHostTrack);
        foreach (var (host, port) in MultiHostPair)
        {
            track.Check($"MultiHostTests' {(port == 5440 ? "primary" : "standby")} at {host}:{port} accepts its login", () =>
            {
                var connectionString = $"Host={host};Port={port};Database=marten_testing;Username=user;password=password";
                try
                {
                    using var conn = openUnpooled(connectionString);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"{host}:{port} is not MultiHostTests' replication pair ({e.Message}). Start it with " +
                        "`docker compose -f src/MultiHostTests/docker-compose.yaml up -d`, after freeing 5440/5441.", e);
                }
            });
        }

        return track;
    }
}
