using System;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.SessionMechanics;

public class session_timeouts : IntegrationContext
{
    [Fact]
    public void should_respect_command_timeout_options()
    {
        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            var session = theStore.QuerySession(new SessionOptions() {Timeout = -1});
        });

        ex.Message.ShouldContain("CommandTimeout can't be less than zero", Case.Insensitive);
    }



    public session_timeouts(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
