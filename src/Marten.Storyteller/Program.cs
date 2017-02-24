using Baseline.Dates;
using StoryTeller;
using StoryTeller.Engine;

namespace Marten.Storyteller
{
    public class Program
    {
        public static void Main(string[] args)
        {
            StorytellerAgent.Run(args);
        }

        public static void FindProblems()
        {
            using (var runner = StorytellerRunner.For<MartenSystem>())
            {
                runner.Run("Linq Queries / Take and Skip part 2");
                //runner.OpenResultsInBrowser();
            }
        }
    }

    public class MartenSystem : NulloSystem
    {
        
    }
}