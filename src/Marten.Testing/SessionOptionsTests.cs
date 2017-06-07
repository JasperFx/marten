using Baseline;
using Marten.Services;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class SessionOptionsTests : IntegratedFixture
    {
        // SAMPLE: ConfigureCommandTimeout
public void ConfigureCommandTimeout(IDocumentStore store)
{
    // Sets the command timeout for this session to 60 seconds
    // The default is 30
    using (var session = store.OpenSession(new SessionOptions {Timeout = 60}))
    {
                
    }
}
        // ENDSAMPLE


        [Fact]
        public void the_default_concurrency_checks_is_enabled()
        {
            new SessionOptions().ConcurrencyChecks
                .ShouldBe(ConcurrencyChecks.Enabled);
        }

        [Fact]
        public void can_choke_on_custom_timeout()
        {

            var options = new SessionOptions() { Timeout = 1 };

            using (var session = theStore.OpenSession(options))
            {
                var e = Assert.Throws<MartenCommandException>(() =>
                {
                    session.Query<QuerySessionTests.FryGuy>("select pg_sleep(2)");
                });

                Assert.Contains("connected party did not properly respond after a period of time", e.InnerException.InnerException.Message);
            }
        }

        [Fact]
        public void can_define_custom_timeout()
        {
            var guy1 = new QuerySessionTests.FryGuy();
            var guy2 = new QuerySessionTests.FryGuy();
            var guy3 = new QuerySessionTests.FryGuy();

            using (var session = theStore.OpenSession())
            {
                session.Store(guy1, guy2, guy3);
                session.SaveChanges();
            }

            var options = new SessionOptions() { Timeout = 15 };

            using (var query = theStore.QuerySession(options).As<QuerySession>())
            {
                query.LoadDocument<QuerySessionTests.FryGuy>(guy2.id).ShouldNotBeNull();
            }
        }
    }
}