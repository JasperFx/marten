using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Acceptance
{
    public class document_inserts: IntegratedFixture
    {
        [Fact]
        public void can_insert_all_new_documents()
        {
            using (var session = theStore.OpenSession())
            {
                session.Insert(Target.GenerateRandomData(99).ToArray());
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Query<Target>().Count().ShouldBe(99);
            }
        }

        [Fact]
        public void can_insert_a_mixed_bag_of_documents()
        {
            var docs = new object[]
            {
                Target.Random(),
                Target.Random(),
                Target.Random(),
                new User(),
                new User(),
                new User(),
                new User()
            };

            using (var session = theStore.OpenSession())
            {
                session.InsertObjects(docs);
                session.SaveChanges();
            }

            using (var query = theStore.QuerySession())
            {
                query.Query<Target>().Count().ShouldBe(3);
                query.Query<User>().Count().ShouldBe(4);
            }
        }

        [Fact]
        public void insert_sad_path()
        {
            var target = Target.Random();

            // SAMPLE: sample-document-insertonly
            using (var session = theStore.OpenSession())
            {
                session.Insert(target);
                session.SaveChanges();
            }
            // ENDSAMPLE

            using (var session = theStore.OpenSession())
            {
                Exception<DocumentAlreadyExistsException>.ShouldBeThrownBy(() =>
                {
                    session.Insert(target);
                    session.SaveChanges();
                });
            }
        }
    }
}
