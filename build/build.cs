using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading;
using Npgsql;
using static System.Globalization.CultureInfo;
using static Bullseye.Targets;
using static SimpleExec.Command;
using static Westwind.Utilities.FileUtils;

namespace martenbuild
{
    internal class MartenBuild
    {
        private static string _framework;
        private static string _configuration;

        private const string DockerConnectionString =
            "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres";

        private static void Main(string[] args)
        {
            _framework = GetFramework();

            var configuration = GetEnvironmentVariable("config");
            configuration = string.IsNullOrEmpty(configuration) ? "debug" : configuration;

            _configuration = configuration;

            var disableTestParallelization = GetEnvironmentVariable("disable_test_parallelization");

            Target("ci", DependsOn("connection", "default"));

            Target("default", DependsOn("mocha", "test", "storyteller"));

            Target("clean", () =>
                EnsureDirectoriesDeleted("results", "artifacts"));

            Target("connection", () =>
                File.WriteAllText("src/Marten.Testing/connection.txt", GetEnvironmentVariable("connection")));

            Target("install", () =>
                RunNpm("install"));

            Target("mocha", DependsOn("install"), () =>
                RunNpm("run test"));

            Target("compile", DependsOn("clean"), () =>
            {
                Run("dotnet",
                    $"build src/Marten.Testing/Marten.Testing.csproj --framework {_framework} --configuration {configuration}");

                Run("dotnet",
                    $"build src/Marten.Schema.Testing/Marten.Schema.Testing.csproj --framework {_framework} --configuration {configuration}");
            });

            Target("compile-noda-time", DependsOn("clean"), () =>
                Run("dotnet", $"build src/Marten.NodaTime.Testing/Marten.NodaTime.Testing.csproj --framework {_framework} --configuration {configuration}"));

            Target("test-noda-time", DependsOn("compile-noda-time"), () =>
                RunTests("Marten.NodaTime.Testing"));

            Target("compile-aspnetcore", DependsOn("clean"), () =>
                RunTests("Marten.AspNetCore.Testing"));


            Target("test-aspnetcore", DependsOn("compile-aspnetcore"), () =>
                RunTests("Marten.AspNetCore.Testing"));


            Target("test-schema", () =>
                RunTests("Marten.Schema.Testing"));

            Target("test-codegen", () =>
            {
                var projectPath = "src/CommandLineRunner";
                Run("dotnet", $"run -- codegen delete", projectPath);
                Run("dotnet", $"run -- codegen write", projectPath);
                Run("dotnet", $"run -- test", projectPath);
            });

            Target("test-marten", DependsOn("compile", "test-noda-time"), () =>
            {
                RunTests("Marten.Testing");
                RunTests("ConfigurationTests");
            });

            Target("test-plv8", DependsOn("compile"), () =>
                RunTests("Marten.PLv8.Testing"));


            // JDM -- I removed test-codegen temporarily during V5 work
            Target("test", DependsOn("test-marten", "test-noda-time", "test-schema", "test-plv8", "test-aspnetcore"));

            Target("storyteller", DependsOn("compile"), () =>
                Run("dotnet", $"run --framework {_framework} --culture en-US", "src/Marten.Storyteller"));

            Target("open_st", DependsOn("compile"), () =>
                Run("dotnet", $"storyteller open --framework {_framework} --culture en-US", "src/Marten.Storyteller"));

            Target("install-mdsnippets", IgnoreIfFailed(() =>
                Run("dotnet", $"tool install -g MarkdownSnippets.Tool")
            ));

            Target("docs", DependsOn("install", "install-mdsnippets"), () => {
                // Run docs site
                RunNpm("run docs");
            });

            Target("docs-build", DependsOn("install", "install-mdsnippets"), () => {
                // Run docs site
                RunNpm("run docs-build");
            });

            Target("docs-import-v3", DependsOn("docs-build"), () =>
            {
                const string branchName = "gh-pages";
                const string docTargetDir = "docs/.vitepress/dist/v3";
                Run("git", $"clone -b {branchName} https://github.com/jasperfx/marten.git {InitializeDirectory(docTargetDir)}");
            });

            Target("clear-inline-samples", () => {
                var files = Directory.GetFiles("./docs", "*.md", SearchOption.AllDirectories);
                var pattern = @"<!-- snippet:(.+)-->[\s\S]*?<!-- endSnippet -->";
                var replacePattern = $"<!-- snippet:$1-->{Environment.NewLine}<!-- endSnippet -->";
                foreach (var file in files)
                {
                    // Console.WriteLine(file);
                    var content = File.ReadAllText(file);

                    if (!content.Contains("<!-- snippet:")) {
                        continue;
                    }

                    var updatedContent = Regex.Replace(content, pattern, replacePattern);
                    File.WriteAllText(file, updatedContent);
                }
            });


            Target("publish-docs-preview", DependsOn("docs-import-v3"), () =>
                RunNpm("run deploy"));

            Target("publish-docs", DependsOn("docs-import-v3"), () =>
                RunNpm("run deploy:prod"));

            Target("benchmarks", () =>
                Run("dotnet", "run --project src/MartenBenchmarks --configuration Release"));

            Target("recordbenchmarks", () =>
            {
                var profile = GetEnvironmentVariable("profile");

                if (!string.IsNullOrEmpty(profile))
                {
                    CopyDirectory("BenchmarkDotNet.Artifacts/results", InitializeDirectory($"benchmarks/{profile}"));
                }
            });

            Target("pack", DependsOn("compile"), ForEach("./src/Marten", "./src/Marten.CommandLine", "./src/Marten.NodaTime", "./src/Marten.PLv8", "./src/Marten.AspNetCore"), project =>
                Run("dotnet", $"pack {project} -o ./artifacts --configuration Release"));

            Target("init-db", () =>
            {
                Run("docker-compose", "up -d");

                WaitForDatabaseToBeReady();

            });

            Target("setup-test-parallelization", () => {
                if (string.IsNullOrEmpty(disableTestParallelization))
                {
                    Console.WriteLine("disable_test_parallelization env var not set, this step is ignored.");
                    return;
                }

                var test_projects = new string[] {
                  "src/Marten.Testing",
                  "src/Marten.Schema.Testing",
                  "src/Marten.NodaTime.Testing"
                };

                foreach (var item in test_projects)
                {
                    var assemblyInfoFile = Path.Join(item, "AssemblyInfo.cs");
                    File.WriteAllText(assemblyInfoFile, $"using Xunit;{Environment.NewLine}[assembly: CollectionBehavior(DisableTestParallelization = {disableTestParallelization})]");
                }
            });

            RunTargetsAndExit(args);
        }

        private static void RunTests(string projectName, string directoryName = "src")
        {
            Run("dotnet", $"test --no-build --no-restore --configuration {_configuration} --framework {_framework} {directoryName}/{projectName}/{projectName}.csproj");
        }

        private static void WaitForDatabaseToBeReady()
        {
            var attempt = 0;
            while (attempt < 10)
                try
                {
                    using (var conn = new NpgsqlConnection(DockerConnectionString))
                    {
                        conn.Open();

                        var cmd = conn.CreateCommand();
                        cmd.CommandText = "create extension if not exists plv8";
                        cmd.ExecuteNonQuery();

                        Console.WriteLine("Postgresql is up and ready!");
                        break;
                    }
                }
                catch (Exception)
                {
                    Thread.Sleep(250);
                    attempt++;
                }
        }

        private static string InitializeDirectory(string path)
        {
            EnsureDirectoriesDeleted(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void EnsureDirectoriesDeleted(params string[] paths)
        {
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    var dir = new DirectoryInfo(path);
                    DeleteDirectory(dir);
                }
            }
        }

        private static void DeleteDirectory(DirectoryInfo baseDir)
        {
            baseDir.Attributes = FileAttributes.Normal;
            foreach (var childDir in baseDir.GetDirectories())
                DeleteDirectory(childDir);

            foreach (var file in baseDir.GetFiles())
                file.IsReadOnly = false;

            baseDir.Delete(true);
        }

        private static void RunNpm(string args) =>
            Run("npm", args, windowsName: "cmd.exe", windowsArgs: $"/c npm {args}");

        private static string GetEnvironmentVariable(string variableName)
        {
            var val = Environment.GetEnvironmentVariable(variableName);

            // Azure devops converts environment variable to upper case and dot to underscore
            // https://docs.microsoft.com/en-us/azure/devops/pipelines/process/variables?view=azure-devops&tabs=yaml%2Cbatch
            // Attempt to fetch variable by updating it
            if (string.IsNullOrEmpty(val))
            {
                val = Environment.GetEnvironmentVariable(variableName.ToUpper().Replace(".", "_"));
            }

            Console.WriteLine(val);

            return val;
        }

        private static string GetFramework()
        {
            var frameworkName = Assembly.GetEntryAssembly().GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName;
            var version = float.Parse(frameworkName.Split('=')[1].Replace("v",""), InvariantCulture.NumberFormat);

            return version < 5.0 ? $"netcoreapp{version.ToString("N1", InvariantCulture.NumberFormat)}" : $"net{version.ToString("N1", InvariantCulture.NumberFormat)}";
        }

        private static Action IgnoreIfFailed(Action action)
        {
            return () =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception.Message);
                }
            };
        }
    }
}
