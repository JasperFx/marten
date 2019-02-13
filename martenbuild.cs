using System;
using System.IO;
using static Bullseye.Targets;
using static SimpleExec.Command;
using static Westwind.Utilities.FileUtils;

namespace martenbuild
{
    class MartenBuild
    {
        private const string BUILD_VERSION = "3.4.0";

        static void Main(string[] args)
        {
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
                Run("dotnet", $"build src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.1 --configuration {configuration}"));

            Target("test", DependsOn("compile"), () =>
                Run("dotnet", $"test src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.1 --configuration {configuration} --no-build"));

            Target("storyteller", DependsOn("compile"), () =>
                Run("dotnet", $"run --framework netcoreapp2.1 --culture en-US", "src/Marten.Storyteller"));

            Target("open_st", DependsOn("compile"), () =>
                Run("dotnet", $"storyteller open --framework netcoreapp2.1 --culture en-US", "src/Marten.Storyteller"));

            Target("docs-restore", () =>
                Run("dotnet", "restore", "tools/stdocs"));

            Target("docs", DependsOn("docs-restore"), () =>
                RunStoryTellerDocs($"run -d ../../documentation -c ../../src -v {BUILD_VERSION}"));

            // Exports the documentation to jasperfx.github.io/marten - requires Git access to that repo though!
            Target("publish", () =>
            {
                const string docTargetDir = "doc-target";

                Run("git", $"clone -b gh-pages https://github.com/jasperfx/marten.git {InitializeDirectory(docTargetDir)}");
                // if you are not using git --global config, un-comment the block below, update and use it
                // Run("git", "config user.email user_email", docTargetDir);
                // Run("git", "config user.name user_name", docTargetDir);

                RunStoryTellerDocs(
                    $"export ../../{docTargetDir} ProjectWebsite -d ../../documentation -c ../../src -v {BUILD_VERSION} --project marten");

                Run("git", "add --all", docTargetDir);
                Run("git", $"commit -a -m \"Documentation Update for {BUILD_VERSION}\" --allow-empty", docTargetDir);
                Run("git", "push origin gh-pages", docTargetDir);
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

            Target("pack", DependsOn("compile"), ForEach("./src/Marten", "./src/Marten.CommandLine"), project =>
                Run("dotnet", $"pack {project} -o ./../../artifacts --configuration Release"));

            RunTargetsAndExit(args);
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

        private static void RunNpm(string args)
        {
            if (Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX)
            {
                Run("cmd.exe", $"/c npm {args}");
            }
            else
            {
                Run("npm", args);
            }
        }

        private static void RunStoryTellerDocs(string args)
        {
            Run("dotnet", "restore", "tools/stdocs");
            Run("dotnet", $"stdocs {args}", "tools/stdocs");
        }
    }
}
