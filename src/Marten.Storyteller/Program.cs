using System;
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
                var results = runner.Run("Multi Tenancy / Querying via Compiled Queries");
                Console.WriteLine(results.Counts);

                //results = runner.Run("Event Store / Async Daemon / Rebuild Projection");
               
                //Console.WriteLine(results.Counts);
                
                runner.OpenResultsInBrowser();
            }
        }
    }


    public class MartenSystem : SimpleSystem
    {
        public MartenSystem()
        {
            ExceptionFormatting.AsText<ShouldAssertException>(x => x.Message);
        }

        private readonly Lazy<AsyncDaemonTestHelper> _daemonHelper = new Lazy<AsyncDaemonTestHelper>(() =>
        {
            var helper = new AsyncDaemonTestHelper();
            helper.LoadAllProjects();
            return helper;
        });

        public override void Dispose()
        {
            if (_daemonHelper.IsValueCreated)
            {
                _daemonHelper.Value.Dispose();
            }
        }

        public override void BeforeEach(SimpleExecutionContext execution, ISpecContext context)
        {
            context.State.Store(_daemonHelper);
        }
    }
}