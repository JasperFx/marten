using DcbLoadTest;

// DCB load and behaviour harness. Run in Release against a database nothing else is using:
//
//   docker run -d --name dcb-loadtest-pg17 --shm-size=512m -p 5433:5432 -e POSTGRES_PASSWORD=postgres \
//       -e POSTGRES_DB=marten_testing postgres:17 postgres -c max_connections=500
//   dotnet run -c Release --project src/DcbLoadTest -- <scenario> [--out results.md] [--key value]...
//
// The connection defaults to port 5433; override with the marten_testing_database env var.
// --modes TagTables,HStore and --scopes TagValue,TagValueAndEventType narrow every scenario's matrix.
// --container <name> is the docker container whose log is searched for "deadlock detected" ("" to skip).

var settings = new Settings(args);
Report.Open(settings);

switch (settings.Scenario)
{
    case "append":
        // --repeats 3 --single-saves 2000 --batch-saves 400 --concurrent-saves 100 --writers 16
        await AppendScenario.Run(settings);
        break;

    case "read":
        // --seed-streams 100000 (x10 events) --iterations 1000 --seed-writers 8 --reseed
        await ReadScenario.Run(settings);
        break;

    case "contend":
        // --concurrency 50 --rounds 5 --app-retries 2 --capacity 100 --book 0,0.2,1 --write-retries on,off
        await ContentionScenario.Run(settings, crossed: false);
        break;

    case "crossed":
        // --concurrency 50 --rounds 5 --classes 4 --book 0.5 --write-retries on,off
        await ContentionScenario.Run(settings, crossed: true);
        break;

    case "growth":
        // --tag-values 200000 --seconds 300 --writers 16 --fillfactors none,70 --sample-seconds 30
        await GrowthScenario.Run(settings);
        break;

    case "compare":
        // The original TagTables vs HStore comparison
        await CompareScenario.Run(settings);
        break;

    default:
        Console.WriteLine("usage: DcbLoadTest <append|read|contend|crossed|growth|compare> [--out file.md] [--key value]...");
        Console.WriteLine("See the comments in Program.cs for each scenario's options.");
        return 1;
}

return 0;
