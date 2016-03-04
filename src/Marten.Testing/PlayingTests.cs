using System;
using System.Data;
using System.Diagnostics;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Testing.Fixtures;
using Marten.Testing.Github;
using Octokit;
using Shouldly;
using StoryTeller;
using StoryTeller.Engine;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class PlayingTests
    {
        public void run_st_spec()
        {
            using (var runner = new SpecRunner<NulloSystem>())
            {
                var results = runner.Run("Event Store/Event Capture/Version a stream as part of event capture");


                runner.OpenResultsInBrowser();
            }
        }
    }
}