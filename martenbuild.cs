using System;
using System.IO;
using static Bullseye.Targets;
using static SimpleExec.Command;
using static Westwind.Utilities.FileUtils;

namespace martenbuild
{
    class MartenBuild
    {
        private const string RESULTS_DIR = "results";
        private const string BUILD_VERSION = "3.0.0";

        static void Main(string[] args)
        {
            OperatingSystem os = Environment.OSVersion;

            PlatformID pid = os.Platform;

            var compileTarget = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("config")) 
                ? "debug" 
                : Environment.GetEnvironmentVariable("config");

            var connection = Environment.GetEnvironmentVariable("connection");

            Target("ci", DependsOn("connection", "default", "pack"));
            
            Target("default", DependsOn("mocha", "test", "storyteller"));

            Target("clean", () =>
            {
                if (Directory.Exists(RESULTS_DIR))
                {
                    Directory.Delete(RESULTS_DIR, true);
                }
                
                if (Directory.Exists("artifacts"))
                {
                    Directory.Delete("artifacts", true);
                }
            });

            Target("connection", () =>
            {
                File.WriteAllText("src/Marten.Testing/connection.txt", connection);
            });

            Target("mocha", () =>
            {
                if (pid == PlatformID.Unix || pid == PlatformID.MacOSX)
                {
                    Run("npm", "install");
                    Run("npm", "run test");
                }
                else
                {
                    Run("cmd.exe", "/c npm install");
                    Run("cmd.exe", "/c npm run test");
                }
            });
            
            Target("compile", DependsOn("clean", "restore"), () =>
            {
                Run("dotnet", $"build src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.1 --configuration {compileTarget}");
            });

            Target("test", DependsOn("compile"), () =>
            {
                Run("dotnet", $"test src/Marten.Testing/Marten.Testing.csproj --framework netcoreapp2.1 --configuration {compileTarget}");
            });

            Target("storyteller", DependsOn("compile"), () =>
            {
                Run("dotnet", $"run --framework netcoreapp2.1 --culture en-US", "src/Marten.Storyteller");
            });

            Target("open_st", DependsOn("compile"), () =>
            {
                Run("dotnet", $"storyteller open --framework netcoreapp2.1 --culture en-US", "src/Marten.Storyteller");
            });

            Target("docs", () =>
            {
                Run("dotnet", "restore", "tools/stdocs");
                Run("dotnet", $"stdocs run -d ../../documentation -v {BUILD_VERSION}", "tools/stdocs");
            });

            // Exports the documentation to jasperfx.github.io/marten - requires Git access to that repo though!
            Target("publish", () =>
            {
                string docTargetDir = "doc-target";
                
                if (Directory.Exists(docTargetDir))
                {
                    Directory.Delete(docTargetDir, true);      
                }

                Directory.CreateDirectory(docTargetDir);
                
                Run("git", $"clone -b gh-pages https://github.com/jasperfx/marten.git {docTargetDir}");
                
                Run("dotnet", "restore", "tools/stdocs");
                Run("dotnet", $"stdocs export ../../{docTargetDir} ProjectWebsite -d ../../documentation -c ../../src -v {BUILD_VERSION} --project marten", "tools/stdocs");
                
                Run("git", "add --all", docTargetDir);
                Run("git", $"commit -a -m \"Documentation Update for {BUILD_VERSION}\" --allow-empty", docTargetDir);
                Run("git", "git push origin gh-pages", docTargetDir);
            });

            Target("restore", () =>
            {
                Run("dotnet", "restore src/Marten.sln");
            });

            Target("benchmarks", DependsOn("restore"), () =>
            {
                Run("dotnet", "run --project src/MartenBenchmarks --configuration Release");
            });

            Target("recordbenchmarks", () =>
            {
                var profile = Environment.GetEnvironmentVariable("profile");

                if (string.IsNullOrEmpty(profile))
                {
                    return;
                }

                var dir = $"benchmarks/{profile}";

                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }

                Directory.CreateDirectory(dir);
                
                CopyDirectory("BenchmarkDotNet.Artifacts/results", dir);
            });
            
            Target("pack", DependsOn("compile"), () =>
            {
                Run("dotnet", "pack ./src/Marten -o ./../../artifacts --configuration Release");
                Run("dotnet", "pack ./src/Marten.CommandLine -o ./../../artifacts --configuration Release");
            });

            RunTargets(args);
        }
    }
}
