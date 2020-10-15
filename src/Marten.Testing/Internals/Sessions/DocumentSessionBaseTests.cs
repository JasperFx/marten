using Castle.DynamicProxy.Contributors;
using Marten.Internal.Sessions;
using Marten.Testing.Harness;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Internals.Sessions
{
    public class DocumentSessionBaseTests : IntegrationContext
    {
        public DocumentSessionBaseTests(DefaultStoreFixture fixture) : base(fixture)
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
    }
}
