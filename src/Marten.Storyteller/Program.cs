using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Dates;
using Marten.Testing.AsyncDaemon;
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


    public class MartenSystem : SimpleSystem
    {
        private AsyncDaemonTestHelper _daemonHelper;

        public MartenSystem()
        {
            ExceptionFormatting.AsText<ShouldAssertException>(x => x.Message);
        }


        public override void Dispose()
        {
            _daemonHelper?.Dispose();
        }

        public override void BeforeEach(SimpleExecutionContext execution, ISpecContext context)
        {
            context.State.Store(_daemonHelper);
        }

        public override Task Warmup()
        {
            return Task.Factory.StartNew(() =>
            {
                _daemonHelper = new AsyncDaemonTestHelper();
                _daemonHelper.LoadAllProjects();
            });

        }
    }
}