using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.CoreFunctionality
{
    public class LongDoc
    {
        public long Id { get; set; }
    }

    public class persist_and_load_documents_with_long_ids_Tests : IntegrationContext
    {
        [Fact]
        public void persist_and_load()
        {
            var LongDoc = new LongDoc { Id = 456 };

            theSession.Store(LongDoc);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                SpecificationExtensions.ShouldNotBeNull(session.Load<LongDoc>(456));

                SpecificationExtensions.ShouldBeNull(session.Load<LongDoc>(222));
            }

        }

        [Fact]
        public void auto_assign_id_for_0_id()
        {
            var doc = new LongDoc { Id = 0 };

            theSession.Store(doc);

            SpecificationExtensions.ShouldBeGreaterThan(doc.Id, 0L);

            var doc2 = new LongDoc { Id = 0 };
            theSession.Store(doc2);

            doc2.Id.ShouldNotBe(0L);

            doc2.Id.ShouldNotBe(doc.Id);
        }

        [Fact]
        public void persist_and_delete()
        {
            var LongDoc = new LongDoc { Id = 567 };

            theSession.Store(LongDoc);
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                session.Delete<LongDoc>((int) LongDoc.Id);
                session.SaveChanges();
            }

            using (var session = theStore.OpenSession())
            {
                SpecificationExtensions.ShouldBeNull(session.Load<LongDoc>(LongDoc.Id));
            }
        }

        [Fact]
        public void load_by_array_of_ids()
        {
            theSession.Store(new LongDoc{Id = 3});
            theSession.Store(new LongDoc{Id = 4});
            theSession.Store(new LongDoc{Id = 5});
            theSession.Store(new LongDoc{Id = 6});
            theSession.Store(new LongDoc{Id = 7});
            theSession.SaveChanges();

            using (var session = theStore.OpenSession())
            {
                session.LoadMany<LongDoc>(4, 5, 6).Count().ShouldBe(3);
            }
        }

        public persist_and_load_documents_with_long_ids_Tests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
