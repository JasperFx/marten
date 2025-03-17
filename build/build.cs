using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Npm;
using Serilog;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution;
    [Parameter] readonly bool DisableTestParallelization = true;
    [Parameter]readonly string Framework;
    [Parameter] readonly string Profile;
    [Parameter] readonly string ConnectionString ="Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres";

    Target Test => _ => _
        .DependsOn(TestBaseLib)
        .DependsOn(TestCore)
        .DependsOn(TestDocumentDb)
        .DependsOn(TestEventSourcing)
        .DependsOn(TestCli)
        .DependsOn(TestLinq)
        .DependsOn(TestMultiTenancy)
        .DependsOn(TestPatching)
        .DependsOn(TestValueTypes)
        .DependsOn(TestCodeGen);

    Target TestExtensions => _ => _
        .DependsOn(TestNodaTime)
        .DependsOn(TestAspnetcore);

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
        .Executes(() => NpmTasks.NpmInstall());
   
    Target Mocha => _ => _
        .ProceedAfterFailure()
        .DependsOn(NpmInstall)
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("test")));

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


    Target TestBaseLib => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/Marten.Testing")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestNodaTime => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/Marten.NodaTime.Testing")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestAspnetcore => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/Marten.AspNetCore.Testing")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestCore => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/CoreTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestCli => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/Marten.CommandLine.Tests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestDocumentDb => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/DocumentDbTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestEventSourcing => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/EventSourcingTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestLinq => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/LinqTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestValueTypes => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/ValueTypeTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestMultiTenancy => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/MultiTenancyTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestPatching => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            DotNetTest(c => c
                .SetProjectFile("src/PatchingTests")
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetFramework(Framework));
        });

    Target TestCodeGen => _ => _
        .ProceedAfterFailure()
        .Executes(() =>
        {
            var codegenCommands = new[] { "codegen delete", "codegen write", "codegen test" };
            foreach (var command in codegenCommands)
            {
                DotNetRun(s => s
                    .SetProjectFile("src/CommandLineRunner")
                    .SetConfiguration(Configuration)
                    .SetFramework("net8.0")
                    .SetApplicationArguments(command)
                );
            }
        });

    Target RebuildDb => _ => _
        .Executes(() =>
        {
            ProcessTasks.StartProcess("docker", "compose down");
            ProcessTasks.StartProcess("docker", "compose up -d");
        });

    Target InitDb => _ => _
        .Executes(() =>
        {
            ProcessTasks.StartProcess("docker", "compose up -d");
            WaitForDatabaseToBeReady();
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
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("deploy")));
    
    Target PublishDocs => _ => _
        .DependsOn(NpmInstall, InstallMdSnippets)
        .Executes(() => NpmTasks.NpmRun(s => s.SetCommand("deploy:prod")));
    
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
                "./src/Marten.NodaTime",
                "./src/Marten.AspNetCore"
            };

            foreach (var project in projects)
            {
                DotNetPack(s => s
                    .SetProject(project)
                    .SetOutputDirectory("./artifacts")
                    .SetConfiguration(Configuration.Release));
            }
        });

    private void WaitForDatabaseToBeReady()
    {
        var attempt = 0;
        while (attempt < 10)
            try
            {
                using (var conn = new Npgsql.NpgsqlConnection(ConnectionString + ";Pooling=false"))
                {
                    conn.Open();

                    var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT 1";
                    cmd.ExecuteNonQuery();

                    Log.Information("Postgresql is up and ready!");
                    break;
                }
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
