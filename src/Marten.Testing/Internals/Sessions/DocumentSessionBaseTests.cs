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
            session.Metadata.ShouldBeNull();

            session.SetMetadata("a", 1);
            session.Metadata.ShouldNotBeNull();
            session.GetMetadata("a").ShouldBe(1);

        }
    }
}
