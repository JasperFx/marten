using System.Diagnostics;
using System.Globalization;
using System.Text;
using Marten;
using Marten.Events;
using Npgsql;

namespace DcbLoadTest;

/// <summary>Command line: <c>&lt;scenario&gt; [--key value]...</c>.</summary>
public sealed class Settings
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public Settings(string[] args)
    {
        Scenario = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "help";
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) continue;
            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
            _values[key] = value;
        }

        ConnectionString = Environment.GetEnvironmentVariable("marten_testing_database")
                           ?? "Host=localhost;Port=5433;Database=marten_testing;Username=postgres;Password=postgres";
    }

    public string Scenario { get; }
    public string ConnectionString { get; }

    /// <summary>The PostgreSQL container, for counting "deadlock detected" in its log. Empty to skip.</summary>
    public string Container => Get("container", "dcb-loadtest-pg17");

    public string? OutPath => _values.TryGetValue("out", out var v) ? v : null;

    public string Get(string key, string fallback) => _values.TryGetValue(key, out var v) ? v : fallback;
    public int Int(string key, int fallback) => _values.TryGetValue(key, out var v) ? int.Parse(v) : fallback;
    public double Double(string key, double fallback)
        => _values.TryGetValue(key, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : fallback;

    public DcbStorageMode[] Modes => Get("modes", "TagTables,HStore").Split(',')
        .Select(Enum.Parse<DcbStorageMode>).ToArray();

    public DcbConflictScope[] Scopes => Get("scopes", "TagValue,TagValueAndEventType").Split(',')
        .Select(Enum.Parse<DcbConflictScope>).ToArray();
}

public static class Stores
{
    private static NpgsqlDataSource? _dataSource;

    /// <summary>
    /// One data source shared by every store, owned here rather than by Marten, so that
    /// <see cref="Pg.SettleStats"/> can close its idle connections. A backend flushes its cumulative
    /// statistics when it exits, but an idle one only every 10 seconds (PGSTAT_IDLE_INTERVAL).
    /// </summary>
    public static NpgsqlDataSource DataSource(Settings settings)
        => _dataSource ??= new NpgsqlDataSourceBuilder(settings.ConnectionString).Build();

    public static string Short(DcbConflictScope scope) => scope switch
    {
        DcbConflictScope.TagValue => "tv",
        DcbConflictScope.TagValueRecordingEventTypes => "tvr",
        DcbConflictScope.TagValueAndEventType => "tvet",
        _ => scope.ToString()
    };

    public static DocumentStore Build(Settings settings, string schema, DcbStorageMode mode, DcbConflictScope scope,
        bool writeRetries = true, int? fillFactor = null)
    {
        return DocumentStore.For(opts =>
        {
            // DcbStorageMode BEFORE Connection(dataSource): setting HStore calls
            // ConfigureNpgsqlDataSourceBuilder, which replaces the data source factory, so a data
            // source supplied earlier is dropped and rebuilt from its password-stripped
            // ConnectionString. The hstore extension is created up front (see Fresh) because this
            // order skips the bootstrap HStore mode adds to Marten's own data source builder.
            opts.Events.DcbStorageMode = mode;
            opts.Connection(DataSource(settings));
            opts.DatabaseSchemaName = schema;
            opts.Events.DatabaseSchemaName = schema;
            opts.Events.DcbConflictScope = scope;
            if (fillFactor.HasValue) opts.Events.DcbTagVersionFillFactor = fillFactor;

            // --event-id-index: HStore (and TagTables' surplus / per-event StartStream) tag writes find
            // their event with `where id = $1`, and mt_events has no index on id unless this is on.
            if (settings.Get("event-id-index", "false") == "true") opts.Events.EnableUniqueIndexOnEventId = true;

            // Marten's write retries replay a commit that failed with 40P01 (and a few other
            // "nothing was left behind" SQLSTATEs). A deadlock then costs deadlock_timeout (1s)
            // and a replay instead of an exception, which is exactly what hides one under load.
            if (!writeRetries) opts.ConfigureWritePolly(_ => { });

            opts.Events.AddEventType<OrderPlaced>();
            opts.Events.AddEventType<OrderShipped>();
            opts.Events.AddEventType<ClassScheduled>();
            opts.Events.AddEventType<SpotBooked>();
            opts.Events.AddEventType<WaitlistJoined>();

            opts.Events.RegisterTagType<CustomerId>("customer");
            opts.Events.RegisterTagType<RegionId>("region");
            opts.Events.RegisterTagType<ClassId>("class");
        });
    }

    /// <summary>Drop the schema and build it again, so every measurement starts from empty tables.</summary>
    public static async Task<DocumentStore> Fresh(Settings settings, string schema, DcbStorageMode mode,
        DcbConflictScope scope, bool writeRetries = true, int? fillFactor = null)
    {
        await Pg.Execute(settings, $"drop schema if exists {schema} cascade");
        await Pg.Execute(settings, "create extension if not exists hstore");
        var store = Build(settings, schema, mode, scope, writeRetries, fillFactor);
        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        return store;
    }
}

/// <summary>Thread-safe latency sample, in milliseconds.</summary>
public sealed class Latencies
{
    private readonly List<double> _values = new();

    public void Add(double ms)
    {
        lock (_values) _values.Add(ms);
    }

    public void Add(TimeSpan elapsed) => Add(elapsed.TotalMilliseconds);

    public void AddRange(Latencies other)
    {
        double[] copy;
        lock (other._values) copy = other._values.ToArray();
        lock (_values) _values.AddRange(copy);
    }

    public int Count
    {
        get { lock (_values) return _values.Count; }
    }

    public double Percentile(double p)
    {
        double[] sorted;
        lock (_values) sorted = _values.ToArray();
        if (sorted.Length == 0) return double.NaN;
        Array.Sort(sorted);
        var rank = (int)Math.Ceiling(p / 100.0 * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    public double Mean
    {
        get
        {
            lock (_values) return _values.Count == 0 ? double.NaN : _values.Average();
        }
    }

    public double Max
    {
        get
        {
            lock (_values) return _values.Count == 0 ? double.NaN : _values.Max();
        }
    }
}

public sealed record TableStats(
    long Inserts,
    long Updates,
    long HotUpdates,
    long LiveTuples,
    long DeadTuples,
    long AutovacuumCount,
    long HeapBytes,
    long IndexBytes)
{
    public TableStats Minus(TableStats before) => this with
    {
        Inserts = Inserts - before.Inserts,
        Updates = Updates - before.Updates,
        HotUpdates = HotUpdates - before.HotUpdates,
        AutovacuumCount = AutovacuumCount - before.AutovacuumCount
    };
}

public static class Pg
{
    public static async Task Execute(Settings settings, string sql)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 0 };
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<long> Scalar(Settings settings, string sql)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 0 };
        var result = await cmd.ExecuteScalarAsync();
        return result is null or DBNull ? 0 : Convert.ToInt64(result);
    }

    /// <summary>Server-side deadlock count for this database — counts deadlocks a retry absorbed, too.</summary>
    public static Task<long> Deadlocks(Settings settings)
        => Scalar(settings, "select deadlocks from pg_stat_database where datname = current_database()");

    public static async Task<TableStats> Stats(Settings settings, string schema, string table)
    {
        // A busy backend flushes its counters at most once a second, an idle one every 10s; call
        // SettleStats first when the numbers must be complete.
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            select n_tup_ins, n_tup_upd, n_tup_hot_upd, n_live_tup, n_dead_tup, autovacuum_count,
                   pg_relation_size(relid), pg_indexes_size(relid)
            from pg_stat_user_tables where schemaname = $1 and relname = $2
            """, conn);
        cmd.Parameters.AddWithValue(schema);
        cmd.Parameters.AddWithValue(table);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return new TableStats(0, 0, 0, 0, 0, 0, 0, 0);
        return new TableStats(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
            reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7));
    }

    /// <summary>
    /// Make every backend's statistics visible: close the idle connections (a backend flushes its counters
    /// as it exits) and give the backends a moment to go. Call only while nothing is running.
    /// </summary>
    public static async Task SettleStats(Settings settings)
    {
        Stores.DataSource(settings).Clear();
        NpgsqlConnection.ClearAllPools();
        await Task.Delay(1500);
    }

    /// <summary>"deadlock detected" lines in the container's log so far, or -1 when docker is not available.</summary>
    public static int DockerDeadlockLines(Settings settings)
    {
        if (string.IsNullOrEmpty(settings.Container)) return -1;
        try
        {
            var psi = new ProcessStartInfo("docker", $"logs {settings.Container}")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var text = stdout.Result + stderr.Result;
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf("deadlock detected", index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index++;
            }

            return count;
        }
        catch
        {
            return -1;
        }
    }
}

/// <summary>Markdown output, mirrored to <c>--out</c> when given.</summary>
public static class Report
{
    private static string? _outPath;

    public static void Open(Settings settings)
    {
        _outPath = settings.OutPath;
        if (_outPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_outPath))!);
        }
    }

    public static void Line(string text = "")
    {
        Console.WriteLine(text);
        if (_outPath != null) File.AppendAllText(_outPath, text + Environment.NewLine);
    }

    /// <summary>Progress chatter: console only.</summary>
    public static void Progress(string text) => Console.WriteLine("    " + text);

    public static void Table(string[] headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.Append("| ").Append(string.Join(" | ", headers)).AppendLine(" |");
        sb.Append('|').Append(string.Join("|", headers.Select(_ => "---"))).AppendLine("|");
        foreach (var row in rows)
        {
            sb.Append("| ").Append(string.Join(" | ", row)).AppendLine(" |");
        }

        Line(sb.ToString());
    }

    public static string Ms(double ms) => double.IsNaN(ms) ? "-" : ms.ToString(ms < 10 ? "N2" : "N1");
    public static string N0(double value) => value.ToString("N0");
    public static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("N1");
    public static string Pct(double ratio) => double.IsNaN(ratio) ? "-" : (ratio * 100).ToString("N1") + "%";

    public static void Header(Settings settings)
    {
        Line($"_Run {DateTime.Now:yyyy-MM-dd HH:mm}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, " +
             $"{System.Environment.ProcessorCount} cores, server: {PgVersion(settings)}_");
        Line();
    }

    private static string PgVersion(Settings settings)
    {
        try
        {
            using var conn = new NpgsqlConnection(settings.ConnectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("show server_version", conn);
            return "PostgreSQL " + cmd.ExecuteScalar();
        }
        catch (Exception e)
        {
            return "unreachable: " + e.Message;
        }
    }
}

public static class Errors
{
    /// <summary>Walk wrappers (AggregateException, MartenCommandException, ...) down to what was actually thrown.</summary>
    public static IEnumerable<Exception> Chain(Exception e)
    {
        var stack = new Stack<Exception>();
        stack.Push(e);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions) stack.Push(inner);
            }
            else if (current.InnerException != null)
            {
                stack.Push(current.InnerException);
            }
        }
    }

    public static bool Is<T>(Exception e) where T : Exception => Chain(e).Any(x => x is T);

    public static string? SqlState(Exception e)
        => Chain(e).OfType<PostgresException>().Select(x => x.SqlState).FirstOrDefault();
}
