using System;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    // This was to address GH-86
    public class ability_to_persist_nested_types_Tests: IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        [Fact]
        public void can_persist_and_load_nested_types()
        {
            var doc1 = new MyDocument();

            theSession.Store(doc1);
            theSession.SaveChanges();

            var doc2 = theSession.Load<MyDocument>(doc1.Id);
            doc2.ShouldNotBeNull();
        }

        public class MyDocument
        {
            public Guid Id = Guid.NewGuid();
        }

        public ability_to_persist_nested_types_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
