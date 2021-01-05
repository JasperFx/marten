using System;
using System.Threading.Tasks;
using Shouldly;
using StoryTeller;
using StoryTeller.Engine;

namespace Marten.Storyteller
{
    public class Program
    {
        public static void Main(string[] args)
        {
            StorytellerAgent.Run(args, new MartenSystem());
        }

        public static void FindProblems()
        {
            using (var runner = StorytellerRunner.For<MartenSystem>())
            {
                //var results = runner.Run("Multi Tenancy / Deleting by Id");
                //var results = runner.Run("Multi Tenancy / Loading Documents by Id");
                var results = runner.Run("Event Store / Async Daemon / Rebuild Projection");
                Console.WriteLine(results.Counts);

                //results = runner.Run("Event Store / Async Daemon / Rebuild Projection");

                //Console.WriteLine(results.Counts);

                runner.OpenResultsInBrowser();
            }
        }
    }

    public class MartenSystem: SimpleSystem
    {

        public MartenSystem()
        {
            ExceptionFormatting.AsText<ShouldAssertException>(x => x.Message);
        }

    }
}
