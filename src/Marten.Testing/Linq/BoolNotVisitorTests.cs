using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Linq
{
    public class BoolNotVisitorTests : IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        private class TestClass
        {
            public TestClass()
            {
                Id = Guid.NewGuid();
            }

            public Guid Id { get; set; }
            public bool Flag { get; set; }
        }

        [Fact]
        public void when_doc_with_bool_false_should_return_records()
        {
            var docWithFlagFalse = new TestClass();

            theSession.Store(docWithFlagFalse);
            theSession.SaveChanges();

            using (var s = theStore.OpenSession())
            {
                var items = s.Query<TestClass>().Where(x => !x.Flag).ToList();

                Assert.Single(items);
                Assert.Equal(docWithFlagFalse.Id, items[0].Id);
            }
        }

        [Fact]
        public void when_doc_with_bool_false_with_serializer_default_value_handling_null_should_return_records()
        {
            var serializer = new JsonNetSerializer();
            serializer.Customize(s =>
            {
                s.DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore;
            });

            StoreOptions(x => x.Serializer(serializer));

            // Note: with serializer settings DefaultValueHandling.Ignore, serialized JSON won't have Flag property
            var docWithFlagFalse = new TestClass();

            theSession.Store(docWithFlagFalse);
            theSession.SaveChanges();

            using (var s = theStore.OpenSession())
            {
                var items = s.Query<TestClass>().Where(x => !x.Flag).ToList();

                Assert.Single(items);
                Assert.Equal(docWithFlagFalse.Id, items[0].Id);
            }
        }

        public BoolNotVisitorTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
