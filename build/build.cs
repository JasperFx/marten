using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Npm;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

partial class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution(GenerateProjects = true)] readonly Solution Solution;
    [Parameter] readonly bool DisableTestParallelization = true;
    [Parameter]readonly string Framework;
    [Parameter] readonly string Profile;
    [Parameter] readonly string ConnectionString ="Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres";
    [Parameter] readonly string Project;

    // Everything that runs against the ordinary marten_testing database. CI does not use this
    // target — .github/workflows/tests.yml gives each project its own job — but a local
    // `./build.sh test` should still mean "run the suite", so anything added below belongs here.
    // TestMultiHost is deliberately absent: it needs the replication pair from
    // src/MultiHostTests/docker-compose.yaml rather than the standard database.
    Target Test => _ => _
        .DependsOn(TestBaseLib)
        .DependsOn(TestCore)
        .DependsOn(TestDocumentDb)
        .DependsOn(TestEventSourcing)
        .DependsOn(TestDaemon)
        .DependsOn(TestModularConfig)
        .DependsOn(TestLinq)
        .DependsOn(TestMultiTenancy)
        .DependsOn(TestTenantPartitionedEvents)
        .DependsOn(TestPatching)
        .DependsOn(TestValueTypes)
        .DependsOn(TestContainerScopedProjections)
        .DependsOn(TestStress)
        .DependsOn(TestCompiledQueries)
        .DependsOn(TestSourceGenerator)
        .DependsOn(TestAotRuntime);

    Target TestExtensions => _ => _
        .DependsOn(TestNodaTime)
        .DependsOn(TestAspnetcore)
        .DependsOn(TestPostGIS)
        .DependsOn(TestPgVector)
        .DependsOn(TestTimescaleDB)
        .DependsOn(TestEntityFrameworkCore)
        .DependsOn(TestMemoryPack);

    Target Init => _ => _
        .Executes(() =>
        { 
            Clean();
            WriteConnectionStringToFile();
            SetupTestParallelization();
        });

    Target Connection => _ => _
        .Executes(() => File.WriteAllText("src/Marten.Testing/connection.txt", ConnectionString));

    Target NpmInstall => _ => _
        .Executes(() => NpmTasks.NpmInstall(c => c
            .AddProcessAdditionalArguments("--loglevel=error")));
   
    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Restore => _ => _
        .DependsOn(Init)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target CompileProject => _ => _
        .DependsOn(Init)
        .Executes(() =>
        {
            if (string.IsNullOrEmpty(Project))
            {
                Log.Error("Project parameter is required. Usage: --project <path-to-project>");
                throw new ArgumentException("Project parameter must be specified");
            }

            Log.Information($"Restoring project: {Project}");
            DotNetRestore(s => s
                .SetProjectFile(Project));

            Log.Information($"Compiling project: {Project}");
            DotNetBuild(s => s
                .SetProjectFile(Project)
                .SetConfiguration(Configuration)
                .SetFramework(Framework)
                .EnableNoRestore());
        });


    // ─── Test targets ──────────────────────────────────────────────────
    //
    // One target per test project, and one CI job per target — see
    // .github/workflows/tests.yml. Every one of them runs through Bobcat's supervisor
    // (build/SupervisedTests.cs) rather than `dotnet test`, so a failure is retried in a fresh
    // process and a pass-on-retry is reported as flaky instead of disappearing into a green tick.
    //
    // If you add a test project, add a target here AND a matrix entry in tests.yml. A target with
    // no job is how seven projects (#5096) sat un-run for as long as they did.
    //
    // TestStress and TestMultiHost are the two deliberate exceptions to the one-job-per-target
    // rule, and each says why at its own declaration below. Deliberate is the operative word:
    // both are named here so "which targets have no CI job" stays a question with a written
    // answer rather than something you find out by grepping the workflow.

    Target TestBaseLib => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.Testing/Marten.Testing.csproj"));

    Target TestNodaTime => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.NodaTime.Testing/Marten.NodaTime.Testing.csproj"));

    Target TestAspnetcore => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.AspNetCore.Testing/Marten.AspNetCore.Testing.csproj"));

    Target TestMemoryPack => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.MemoryPack.Tests/Marten.MemoryPack.Tests.csproj"));

    Target TestPostGIS => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.PostGIS.Tests/Marten.PostGIS.Tests.csproj"));

    Target TestPgVector => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.PgVector.Tests/Marten.PgVector.Tests.csproj"));

    Target TestTimescaleDB => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.TimescaleDB.Tests/Marten.TimescaleDB.Tests.csproj"));

    Target TestCore => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/CoreTests/CoreTests.csproj"));

    Target TestDocumentDb => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/DocumentDbTests/DocumentDbTests.csproj"));

    Target TestEventSourcing => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/EventSourcingTests/EventSourcingTests.csproj"));

    // The async daemon suite. Ran outside the Nuke build until #5096 — daemon.yml shelled out to
    // `dotnet test` directly — which meant the single most timing-sensitive suite in the repo was
    // also the one with no retry story at all.
    Target TestDaemon => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/DaemonTests/DaemonTests.csproj"));

    Target TestModularConfig => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/ModularConfigTests/ModularConfigTests.csproj"));

    Target TestLinq => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/LinqTests/LinqTests.csproj"));

    Target TestValueTypes => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/ValueTypeTests/ValueTypeTests.csproj"));

    Target TestMultiTenancy => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/MultiTenancyTests/MultiTenancyTests.csproj"));

    // #4617: dedicated project for the UseTenantPartitionedEvents feature
    // surface. Single-store shared fixtures (string + guid) + per-test unique
    // tenant ids — the schema-creation race lives in the partition-CREATE
    // path, so isolation is on tenant not schema. See AssemblyInfo.cs for the
    // DisableTestParallelization rationale.
    Target TestTenantPartitionedEvents => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/TenantPartitionedEventsTests/TenantPartitionedEventsTests.csproj"));

    Target TestPatching => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/PatchingTests/PatchingTests.csproj"));

    // ─── Projects wired up by #5096 ────────────────────────────────────
    //
    // These had no target and no job, so nothing ever ran them. Counts below are `[Fact]`/
    // `[Theory]` attributes, which undercount theories with many cases.

    /// <summary>Container-scoped projection registration and lifetime (~62 tests).</summary>
    Target TestContainerScopedProjections => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/ContainerScopedProjectionTests/ContainerScopedProjectionTests.csproj"));

    /// <summary>
    /// Multi-store hosts, database creation, reset-all-data (29 tests).
    ///
    /// <para>Has NO job in tests.yml, by decision on 2026-08-08. This is the one suite whose work
    /// is to be hostile to its own infrastructure — it calls <c>ResetAllData</c> mid-run and
    /// creates and drops whole databases — which makes it a poor thing to gate a pull request on
    /// even now that it passes cleanly. Run it deliberately with <c>./build.sh test-stress</c>;
    /// it is still part of the aggregate <see cref="Test"/> target, so a local full run covers
    /// it.</para>
    ///
    /// <para>Kept as a target rather than dropped: #5096 was about ten suites that nobody could
    /// run because nothing named them. An excluded suite you can still invoke by name is a
    /// different thing from a dark one.</para>
    /// </summary>
    Target TestStress => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/StressTests/StressTests.csproj"));

    /// <summary>Marten/EF Core interop over a shared connection (~29 tests).</summary>
    Target TestEntityFrameworkCore => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.EntityFrameworkCore.Tests/Marten.EntityFrameworkCore.Tests.csproj"));

    /// <summary>
    /// The compiled-query source generator (~19 tests). Pure Roslyn — no Postgres needed, which is
    /// why its job in tests.yml runs without a database service.
    /// </summary>
    Target TestSourceGenerator => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/Marten.SourceGenerator.Tests/Marten.SourceGenerator.Tests.csproj"));

    /// <summary>The source-generated compiled query path, correctness and perf gates (~9 tests).</summary>
    Target TestCompiledQueries => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/CompiledQueryTests/CompiledQueryTests.csproj"));

    /// <summary>
    /// The RUNTIME Native AOT gate (#5328). Not an xUnit project: it publishes
    /// src/Marten.AotRuntimeSmoke natively for the host RID and executes the binary against the
    /// ordinary test database, so a read path that only breaks once the JIT is gone fails here.
    /// src/Marten.AotSmoke stays the build-time analyzer gate; it cannot see this class of defect
    /// because the offending code analyzes clean and throws one line later.
    ///
    /// Needs a native toolchain for the host platform (clang + zlib on Linux; Xcode command line
    /// tools on macOS; the VS C++ workload on Windows) — see the aot-runtime job in tests.yml.
    /// </summary>
    Target TestAotRuntime => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            const string projectPath = "src/Marten.AotRuntimeSmoke/Marten.AotRuntimeSmoke.csproj";
            const string framework = "net10.0";
            var rid = RuntimeInformation.RuntimeIdentifier;

            Log.Information("Publishing {Project} natively for {Rid} — the ILC compile takes a few minutes",
                projectPath, rid);

            DotNetPublish(s => s
                .SetProject(projectPath)
                .SetConfiguration(Configuration)
                .SetFramework(framework)
                .SetRuntime(rid)
                .SetSelfContained(true));

            var executable = RootDirectory / "src" / "Marten.AotRuntimeSmoke" / "bin" / Configuration / framework /
                             rid / "publish" / (OperatingSystem.IsWindows()
                                 ? "Marten.AotRuntimeSmoke.exe"
                                 : "Marten.AotRuntimeSmoke");

            if (!File.Exists(executable))
            {
                throw new InvalidOperationException(
                    $"The native binary was never produced at {executable}. Either PublishAot did not run for this RID, or the native toolchain is missing — this target needs clang + zlib on Linux, Xcode command line tools on macOS, or the VS C++ workload on Windows.");
            }

            var process = ProcessTasks.StartProcess(executable, workingDirectory: RootDirectory);
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "The Marten AOT runtime smoke failed. A document read path needs runtime code generation again — see the output above for which one.");
            }
        });

    /// <summary>
    /// Primary/standby read routing (~2 tests). Needs the streaming-replication pair from
    /// src/MultiHostTests/docker-compose.yaml on ports 5440/5441, not the ordinary test database —
    /// see the multi-host job in tests.yml.
    /// </summary>
    Target TestMultiHost => _ => _
        .ProceedAfterFailure()
        .Executes(() => RunTestProject("src/MultiHostTests/MultiHostTests.csproj"));

    Target RebuildDb => _ => _
        .Executes(() =>
        {
            ProcessTasks.StartProcess("docker", "compose down");
            ProcessTasks.StartProcess("docker", "compose up -d");
        });

    Target InitDb => _ => _
        .Executes(async () =>
        {
            ProcessTasks.StartProcess("docker", "compose up -d");
            await WaitForDatabaseToBeReady();
        });
    
    Target InstallMdSnippets => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            const string toolName = "markdownSnippets.tool";
            
            if (IsDotNetToolInstalled(toolName))
            {
                Log.Information($"{toolName} is already installed, skipping this step.");
                return;
            }
            
            DotNetToolInstall(c => c
                .SetPackageName(toolName)
                .EnableGlobal());
        });
    
    Target Docs => _ => _
        .DependsOn(NpmInstall, InstallMdSnippets)
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("docs")));

    Target DocsBuild => _ => _
        .DependsOn(NpmInstall, InstallMdSnippets)
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("docs-build")));

    Target CreateFreightShippingTutorialZip => _ => _
        .DependsOn(DocsBuild)
        .Executes(() =>
        {
            Log.Information("Skipping NUKE zip creation; VitePress build plugin creates freight-shipping-tutorial.zip.");
        });

    Target ClearInlineSamples => _ => _
        .Executes(() =>
        {
            var files = Directory.GetFiles("./docs", "*.md", SearchOption.AllDirectories);
            var pattern = @"<!-- snippet:(.+)-->[\s\S]*?<!-- endSnippet -->";
            var replacePattern = $"<!-- snippet:$1-->{Environment.NewLine}<!-- endSnippet -->";
            foreach (var file in files)
            {
                // Console.WriteLine(file);
                var content = File.ReadAllText(file);

                if (!content.Contains("<!-- snippet:"))
                {
                    continue;
                }

                var updatedContent = Regex.Replace(content, pattern, replacePattern);
                File.WriteAllText(file, updatedContent);
            }
        });
    
    Target PublishDocsPreview => _ => _
        .DependsOn(NpmInstall, InstallMdSnippets)
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("deploy-preview")));

    Target PublishDocs => _ => _
        .DependsOn(NpmInstall, InstallMdSnippets, DocsBuild)
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("deploy")));
    
    Target Benchmarks => _ => _
        .Executes(() =>
        {
            DotNetRun(s => s
                .SetProjectFile(Solution.GetProject("MartenBenchmarks"))
                .SetConfiguration(Configuration.Release)
                .SetFramework(Framework)
            );
        });

    Target RecordBenchmarks => _ => _
        .Executes(() =>
        {
            if (!string.IsNullOrEmpty(Profile))
            {
                var resultsDir = AbsolutePath.Create($"benchmarks/{Profile}");
                resultsDir.CreateOrCleanDirectory();
                // CopyDirectory("BenchmarkDotNet.Artifacts/results", resultsDir);
            }
        });
    
    Target Pack => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            var projects = new[]
            {
                "./src/Marten",
                "./src/Marten.Newtonsoft",
                "./src/Marten.NodaTime",
                "./src/Marten.AspNetCore",
                "./src/Marten.EntityFrameworkCore",
                "./src/Marten.SourceGenerator",
                // New optional companion packages — must be listed here so
                // the existing on-manual-do-nuget-publish.yml workflow picks
                // them up. Without this they'd silently never reach NuGet.
                "./src/Marten.PostGIS",        // PostGIS spatial support (#4576)
                "./src/Marten.PgVector",       // pgvector similarity search (#4576)
                // Marten.TimescaleDB folded into core Marten (#4980) — the feature ships in
                // the core assembly (opt-in via UseTimescaleDB()), so there is no separate package.
                "./src/Marten.MemoryPack"      // binary event serialization (#4515 / #4578)
            };

            foreach (var project in projects)
            {
                DotNetPack(s => s
                    .SetProject(project) 
                    .SetOutputDirectory("./artifacts")
                    .SetConfiguration(Configuration.Release));
            }
        });

    private Dictionary<string, string[]> ReferencedProjects = new()
    {
        { "jasperfx", ["JasperFx", "JasperFx.Events", "EventTests", "JasperFx.RuntimeCompiler"] },
        { "weasel", ["Weasel.Core", "Weasel.Postgresql"] },
        {"lamar", ["Lamar", "Lamar.Microsoft.DependencyInjection"]}
    };

    string[] Nugets = ["JasperFx", "JasperFx.Events", "JasperFx.RuntimeCompiler", "Weasel.Postgresql"];
    
    Target Attach => _ => _.Executes(() =>
    {
        foreach (var pair in ReferencedProjects)
        {
            foreach (var projectName in pair.Value)
            {
                addProject(pair.Key, projectName);
            }
        }

        var marten = Solution.Marten.Path;
        foreach (var nuget in Nugets)
        {
            DotNet($"remove {marten} package {nuget}");
        }
    });

    Target Detach => _ => _.Executes(() =>
    {
        foreach (var pair in ReferencedProjects)
        {
            foreach (var projectName in pair.Value)
            {
                removeProject(pair.Key, projectName);
            }
        }
        
        var marten = Solution.Marten.Path;
        foreach (var nuget in Nugets)
        {
            DotNet($"add {marten} package {nuget} --prerelease");
        }
    });

    private void addProject(string repository, string projectName)
    {
        var path =  Path.GetFullPath($"../{repository}/src/{projectName}/{projectName}.csproj");;
        var slnPath = Solution.Path;
        DotNet($"sln {slnPath} add {path} --solution-folder Attached");
        
        if (Nugets.Contains(projectName))
        {
            var marten = Solution.Marten.Path;
            DotNet($"add {marten} reference {path}");
        }
    }
    
    private void removeProject(string repository, string projectName)
    {
        var path =  Path.GetFullPath($"../{repository}/src/{projectName}/{projectName}.csproj");

        if (Nugets.Contains(projectName))
        {
            var marten = Solution.Marten.Path;
            DotNet($"remove {marten} reference {path}");
        }
        
        var slnPath = Solution.Path;
        DotNet($"sln {slnPath} remove {path}");
        

    }

    private async Task WaitForDatabaseToBeReady()
    {
        var attempt = 0;
        while (attempt < 10)
            try
            {
                await using var conn = new Npgsql.NpgsqlConnection(ConnectionString + ";Pooling=false");
                await conn.OpenAsync();

                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1";
                await cmd.ExecuteNonQueryAsync();

                Log.Information("Postgresql is up and ready!");
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while waiting for the database to be ready");
                Thread.Sleep(250);
                attempt++;
            }
    }
    
    bool IsDotNetToolInstalled(string toolName)
    {
        var process = ProcessTasks.StartProcess("dotnet", "tool list -g", logOutput: false);
        process.AssertZeroExitCode();
        var output = process.Output.Select(x => x.Text).ToList();

        return output.Any(line => line.Contains(toolName, StringComparison.OrdinalIgnoreCase));
    }

    static void Clean()
    {
        var results = AbsolutePath.Create("results");
        var artifacts = AbsolutePath.Create("artifacts");
        results.CreateOrCleanDirectory();
        artifacts.CreateOrCleanDirectory();
    }

    void WriteConnectionStringToFile()
    {
        File.WriteAllText("src/Marten.Testing/connection.txt", ConnectionString);
    }

    void SetupTestParallelization()
    {
        if (!DisableTestParallelization)
        {
            Log.Information("DISABLE_TEST_PARALLELIZATION env var not set, this step is ignored.");
            return;
        }
        else
        {
            Log.Information($"DISABLE_TEST_PARALLELIZATION={DisableTestParallelization}");
        }

        var testProjects = new[]
        {
            "src/Marten.Testing",
            "src/Marten.NodaTime.Testing",
            "src/EventSourcingTests",
            "src/DocumentDbTests",
            "src/CoreTests",
            "src/Marten.AspNetCore.Testing",
            "src/ValueTypeTests",
            "src/LinqTests"
        };

        foreach (var item in testProjects)
        {
            var assemblyInfoFile = Path.Combine(item, "AssemblyInfo.cs");
            File.WriteAllText(assemblyInfoFile, $"using Xunit;{Environment.NewLine}[assembly: CollectionBehavior(DisableTestParallelization = {DisableTestParallelization.ToString().ToLower()})]");
        }
    }
}
