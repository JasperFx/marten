using System;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.SessionMechanics
{
    public class session_timeouts : IntegrationContext
	{
		[Fact]
		public void should_respect_command_timeout_options()
        {
            var ex = Exception<ArgumentOutOfRangeException>.ShouldBeThrownBy(() =>
            {
                var session = theStore.QuerySession(new SessionOptions() {Timeout = -1});
            });

            ex.Message.ShouldContain("CommandTimeout can't be less than zero");
        }



        public session_timeouts(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
