using StoryTeller;
using StoryTeller.Engine;

namespace Marten.Testing
{
    public class StorytellerHarness
    {
        public void run_st_spec()
        {
            using (var runner = new SpecRunner<NulloSystem>())
            {
                var results = runner.Run("Linq Queries/DateTime querying");


                runner.OpenResultsInBrowser();
            }
        }
    }
}