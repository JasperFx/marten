using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Metadata;

public class getting_and_setting_headers : IntegrationContext
{
    public getting_and_setting_headers(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public void set_and_get_metadata()
    {
        var session = (DocumentSessionBase)theSession;
        session.Headers.ShouldBeNull();

        session.SetHeader("a", 1);
        session.Headers.ShouldNotBeNull();
        session.GetHeader("a").ShouldBe(1);

    }

    [Fact]
    public void get_empty_metadata()
    {
        // Fixes Issue #2100
        var session = (DocumentSessionBase)theSession;

        session.GetHeader("a").ShouldBeNull();
        session.GetHeader("b").ShouldBeNull();

        session.SetHeader("a", 1);

        session.GetHeader("a").ShouldBe(1);
        session.GetHeader("b").ShouldBeNull();
    }
}