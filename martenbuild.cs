using System;
using System.IO;
using static Bullseye.Targets;
using static SimpleExec.Command;
using static Westwind.Utilities.FileUtils;

namespace martenbuild
{
    class MartenBuild
    {
        private const string BUILD_VERSION = "3.0.0";

        static void Main(string[] args)
        {
            var platformID = Environment.OSVersion.Platform;

            var configuration = Environment.GetEnvironmentVariable("config");
            configuration = string.IsNullOrEmpty(configuration) ? "debug" : configuration;

            Target("ci", DependsOn("connection", "default", "pack"));

            Target("default", DependsOn("mocha", "test", "storyteller"));

            Target("clean", () =>
            {
                EnsureDirectoryDeleted("results");
                EnsureDirectoryDeleted("artifacts");
            });

            Target("connection", () =>
            {
                File.WriteAllText("src/Marten.Testing/connection.txt", Environment.GetEnvironmentVariable("connection"));
            });

            Target("install", () =>
            {
                if (platformID == PlatformID.Unix || platformID == PlatformID.MacOSX)
                {
                    Run("npm", "install");
                }
                else
                {
                    Run("cmd.exe", "/c npm install");
                }
            });

            Target("mocha", DependsOn("install"), () =>
            {
                if (platformID == PlatformID.Unix || platformID == PlatformID.MacOSX)
                {
                    Run("npm", "run test");
                }
                else
                {
                    Run("cmd.exe", "/c npm run test");
                }
            });

            Target("compile", DependsOn("clean"), () =>
            {
                Run("dotnet", $"build src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.1 --configuration {configuration}");
            });

            Target("test", DependsOn("compile"), () =>
            {
                Run("dotnet", $"test src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.1 --configuration {configuration}");
            });

            Target("storyteller", DependsOn("compile"), () =>
            {
                Run("dotnet", $"run --framework netcoreapp2.1 --culture en-US", "src/Marten.Storyteller");
            });

            Target("open_st", DependsOn("compile"), () =>
            {
                Run("dotnet", $"storyteller open --framework netcoreapp2.1 --culture en-US", "src/Marten.Storyteller");
            });

            Target("docs-restore", () =>
            {
                Run("dotnet", "restore", "tools/stdocs");
            });

            Target("docs", DependsOn("docs-restore"), () =>
            {
                Run("dotnet", $"stdocs run -d ../../documentation -c ../../src -v {BUILD_VERSION}", "tools/stdocs");
            });

            // Exports the documentation to jasperfx.github.io/marten - requires Git access to that repo though!
            Target("publish", () =>
            {
                var docTargetDir = InitializeDirectory("doc-target");

                Run("git", $"clone -b gh-pages https://github.com/jasperfx/marten.git {docTargetDir}");

                Run("dotnet", "restore", "tools/stdocs");
                Run("dotnet", $"stdocs export ../../{docTargetDir} ProjectWebsite -d ../../documentation -c ../../src -v {BUILD_VERSION} --project marten", "tools/stdocs");

                Run("git", "add --all", docTargetDir);
                Run("git", $"commit -a -m \"Documentation Update for {BUILD_VERSION}\" --allow-empty", docTargetDir);
                Run("git", "git push origin gh-pages", docTargetDir);
            });

            Target("benchmarks", () =>
            {
                Run("dotnet", "run --project src/MartenBenchmarks --configuration Release");
            });

            Target("recordbenchmarks", () =>
            {
                var profile = Environment.GetEnvironmentVariable("profile");

                if (!string.IsNullOrEmpty(profile))
                {
                    CopyDirectory("BenchmarkDotNet.Artifacts/results", InitializeDirectory($"benchmarks/{profile}"));
                }
            });

            Target("pack", DependsOn("compile"), ForEach("./src/Marten", "./src/Marten.CommandLine"), project =>
            {
                Run("dotnet", $"pack {project} -o ./../../artifacts --configuration Release");
            });

            RunTargets(args);
        }

        private static string InitializeDirectory(string path)
        {
            EnsureDirectoryDeleted(path);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void EnsureDirectoryDeleted(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
