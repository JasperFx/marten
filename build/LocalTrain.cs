using System.IO;
using System.Text.RegularExpressions;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

// Pin Marten to locally packed upstream packages for the .NET 11 release train, before the nuget.org
// replay (JasperFx/marten#5727). Pack's --version / --feed live next to Pack in build.cs.
partial class Build
{
    // Pin the JasperFx* and Weasel* packages in Directory.Packages.props to locally packed versions, and
    // register the local feed as a NuGet source. Run it again with the released versions to put the pins back.
    Target TakeLocalUpstream => _ => _
        .Executes(() =>
        {
            Assert.True(!string.IsNullOrEmpty(UpstreamVersion) || !string.IsNullOrEmpty(WeaselVersion),
                "Pass --upstream-version (JasperFx) and/or --weasel-version");

            var path = RootDirectory / "Directory.Packages.props";
            var text = File.ReadAllText(path);
            text = pin(text, "JasperFx", UpstreamVersion);
            text = pin(text, "Weasel", WeaselVersion);
            File.WriteAllText(path, text);

            if (!string.IsNullOrEmpty(Feed))
            {
                // nuget.config is gitignored: the local feed is machine state, not something to commit.
                var config = RootDirectory / "nuget.config";
                var feed = Path.GetFullPath(Feed);
                if (!File.Exists(config) || !File.ReadAllText(config).Contains(feed))
                {
                    if (File.Exists(config)) DotNet($"nuget remove source critter-local --configfile {config}");
                    else File.WriteAllText(config, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<configuration />\n");
                    DotNet($"nuget add source {feed} --name critter-local --configfile {config}");
                }
            }
        });

    static string pin(string text, string family, string version)
    {
        if (string.IsNullOrEmpty(version)) return text;

        var pattern = $"(<PackageVersion Include=\"{family}(?:\\.[A-Za-z.]+)?\" Version=\")[^\"]+(\")";
        Assert.True(Regex.IsMatch(text, pattern), $"Directory.Packages.props pins no {family} package");
        Serilog.Log.Information("Pinned {Family}* to {Version}", family, version);
        return Regex.Replace(text, pattern, $"${{1}}{version}$2");
    }
}
