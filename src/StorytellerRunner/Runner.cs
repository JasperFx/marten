using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Storyteller;
using Xunit;

namespace StorytellerRunner
{
    // This only exists as a hook to dispose the static
    // StorytellerRunner that is hosting the underlying
    // system under test at the end of all the spec
    // executions
    public class StorytellerFixture : IDisposable
    {
        public void Dispose()
        {
            Runner.SpecRunner.Dispose();
        }
    }

    public class Runner : IClassFixture<StorytellerFixture>
    {
        internal static readonly StoryTeller.StorytellerRunner SpecRunner;

        static Runner()
        {
            // I'll admit this is ugly, but this establishes where the specification
            // files live in the real StorytellerSpecs project
            var directory = AppContext.BaseDirectory
                .ParentDirectory()
                .ParentDirectory()
                .ParentDirectory()
                .ParentDirectory()
                .AppendPath("Marten.Storyteller")
                .AppendPath("Specs");

            SpecRunner = new StoryTeller.StorytellerRunner(new MartenSystem(), directory);
        }

        // Discover all the known Storyteller specifications
        public static IEnumerable<object[]> GetFiles()
        {
            var specifications = SpecRunner.Hierarchy.Specifications.GetAll();
            return specifications.Select(x => new object[] {x.path}).ToArray();
        }

        // Use a touch of xUnit.Net magic to be able to kick off and
        // run any Storyteller specification through xUnit
        [Theory]
        [MemberData(nameof(GetFiles))]
        public void run_specification(string path)
        {
            var results = SpecRunner.Run(path);
            if (!results.Counts.WasSuccessful())
            {
                SpecRunner.OpenResultsInBrowser();
                throw new Exception(results.Counts.ToString());
            }
        }
    }
}
