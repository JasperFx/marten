using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using Npgsql;
using static Bullseye.Targets;
using static SimpleExec.Command;
using static Westwind.Utilities.FileUtils;

namespace martenbuild
{
    internal class MartenBuild
    {
        private const string BUILD_VERSION = "3.12.0";

        private const string DockerConnectionString =
            "Host=localhost;Port=5432;Database=marten_testing;Username=postgres;password=postgres";

        private static void Main(string[] args)
        {
            var framework = GetFramework();

            var configuration = Environment.GetEnvironmentVariable("config");
            configuration = string.IsNullOrEmpty(configuration) ? "debug" : configuration;

            Target("ci", DependsOn("connection", "default", "pack"));

            Target("default", DependsOn("mocha", "test", "storyteller"));

            Target("clean", () =>
                EnsureDirectoriesDeleted("results", "artifacts"));

            Target("connection", () =>
                File.WriteAllText("src/Marten.Testing/connection.txt", Environment.GetEnvironmentVariable("connection")));

            Target("install", () =>
                RunNpm("install"));

            Target("mocha", DependsOn("install"), () =>
                RunNpm("run test"));

            Target("compile", DependsOn("clean"), () =>
            {
                Run("dotnet",
                    $"build src/Marten.Testing/Marten.Testing.csproj --framework {framework} --configuration {configuration}");

                Run("dotnet",
                    $"build src/Marten.Schema.Testing/Marten.Schema.Testing.csproj --configuration {configuration}");
            });

            Target("compile-noda-time", DependsOn("clean"), () =>
                Run("dotnet", $"build src/Marten.NodaTime.Testing/Marten.NodaTime.Testing.csproj --framework {framework} --configuration {configuration}"));

            Target("test-noda-time", DependsOn("compile-noda-time"), () =>
                Run("dotnet", $"test src/Marten.NodaTime.Testing/Marten.NodaTime.Testing.csproj --framework {framework} --configuration {configuration} --no-build"));

            Target("test-marten", DependsOn("compile", "test-noda-time"), () =>
                Run("dotnet", $"test src/Marten.Testing/Marten.Testing.csproj --framework {framework} --configuration {configuration} --no-build"));

            Target("test", DependsOn("test-marten", "test-noda-time"));

            Target("storyteller", DependsOn("compile"), () =>
                Run("dotnet", $"run --framework {framework} --culture en-US", "src/Marten.Storyteller"));

            Target("open_st", DependsOn("compile"), () =>
                Run("dotnet", $"storyteller open --framework {framework} --culture en-US", "src/Marten.Storyteller"));

            Target("docs", () =>
                Run("dotnet", $"stdocs run -d documentation -c src -v {BUILD_VERSION}"));

            Target("publish-docs", () =>
            {
                // Exports the documentation to jasperfx.github.io/marten - requires Git access to that repo though!
                PublishDocs(branchName: "gh-pages", exportWithGithubProjectPrefix: true);
                // Exports the documentation to Netlify - martendb.io - requires Git access to that repo though!
                PublishDocs(branchName: "gh-pages-netlify", exportWithGithubProjectPrefix: false);
            });

            Target("benchmarks", () =>
                Run("dotnet", "run --project src/MartenBenchmarks --configuration Release"));

            Target("recordbenchmarks", () =>
            {
                var profile = Environment.GetEnvironmentVariable("profile");

                if (!string.IsNullOrEmpty(profile))
                {
                    CopyDirectory("BenchmarkDotNet.Artifacts/results", InitializeDirectory($"benchmarks/{profile}"));
                }
            });

            Target("pack", DependsOn("compile"), ForEach("./src/Marten", "./src/Marten.CommandLine", "./src/Marten.NodaTime"), project =>
                Run("dotnet", $"pack {project} -o ./../../artifacts --configuration Release"));

            Target("init-db", () =>
            {
                Run("docker-compose", "up -d");

                WaitForDatabaseToBeReady();

            });

            RunTargetsAndExit(args);
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

        private static void PublishDocs(string branchName, bool exportWithGithubProjectPrefix, string docTargetDir = "doc-target")
        {
            Run("git", $"clone -b {branchName} https://github.com/jasperfx/marten.git {InitializeDirectory(docTargetDir)}");
            // if you are not using git --global config, un-comment the block below, update and use it
            // Run("git", "config user.email user_email", docTargetDir);
            // Run("git", "config user.name user_name", docTargetDir);

            if (exportWithGithubProjectPrefix)
                Run("dotnet", $"stdocs export {docTargetDir} ProjectWebsite -d documentation -c src -v {BUILD_VERSION} --project marten");
            else
                Run("dotnet", $"stdocs export {docTargetDir} Website -d documentation -c src -v {BUILD_VERSION}");

            Run("git", "add --all", docTargetDir);
            Run("git", $"commit -a -m \"Documentation Update for {BUILD_VERSION}\" --allow-empty", docTargetDir);
            Run("git", $"push origin {branchName}", docTargetDir);
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

        private static string GetFramework()
        {
            var frameworkName = Assembly.GetEntryAssembly().GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName;
            return frameworkName.Replace(",Version=v", "").Replace(".NET", "NET").ToLower();
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
    }
}
