using System;
using System.Linq;
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

        //[Fact] doesn't play nicely on Travis
        public void can_choke_on_custom_timeout()
        {

            var options = new SessionOptions() { Timeout = 1 };

            using (var session = theStore.OpenSession(options))
            {
                var e = Assert.Throws<MartenCommandException>(() =>
                {
                    session.Query<FryGuy>("select pg_sleep(2)");
                });

                Assert.Contains("connected party did not properly respond after a period of time", e.InnerException.InnerException.Message);
            }
        }

        [Fact]
        public void default_timeout_should_be_npgsql_default_ie_30()
        {
	        var options = new SessionOptions();

	        using (var query = theStore.QuerySession(options).As<QuerySession>())
	        {
		        var cmd = query.Query<FryGuy>().Explain();
		        Assert.Equal(30, cmd.Command.CommandTimeout);
	        }
        }


		// Remarks: this test was basically asserting nothing related before.
		[Fact]		
        public void can_define_custom_timeout()
        {            
            var options = new SessionOptions() { Timeout = 15 };

            using (var query = theStore.QuerySession(options).As<QuerySession>())
            {
	            var cmd = query.Query<FryGuy>().Explain();
				Assert.Equal(15, cmd.Command.CommandTimeout);
            }
        }

        [Fact]
        public void can_define_custom_timeout_via_pgcstring()
        {
	        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

	        connectionStringBuilder.CommandTimeout = 1;

	        var documentStore = DocumentStore.For(c =>
	        {
		        c.Connection(connectionStringBuilder.ToString());
	        });

	        using (var query = documentStore.OpenSession())
	        {
				var cmd = query.Query<FryGuy>().Explain();
				Assert.Equal(1, cmd.Command.CommandTimeout);
				Assert.Equal(1, query.Connection.CommandTimeout);
	        }
        }

        [Fact]
        public void can_override_pgcstring_timeout_in_sessionoptions()
        {
	        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(ConnectionSource.ConnectionString);

	        connectionStringBuilder.CommandTimeout = 1;

	        var documentStore = DocumentStore.For(c =>
	        {
		        c.Connection(connectionStringBuilder.ToString());
	        });

	        var options = new SessionOptions() { Timeout = 60 };

			using (var query = documentStore.OpenSession(options))
	        {
		        var cmd = query.Query<FryGuy>().Explain();
		        Assert.Equal(60, cmd.Command.CommandTimeout);
		        Assert.Equal(1, query.Connection.CommandTimeout);
	        }
        }

		public class FryGuy
        {
            public Guid Id;
        }
    }
}