using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class query_by_sql_where_clause_Tests
    {
        public query_by_sql_where_clause_Tests()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();
            }
        }

        [Fact]
        public void query_with_select_in_query()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentStore>().OpenSession())
                {
                    var u = new User { FirstName = "Jeremy", LastName = "Miller" };
                    session.Store(u);
                    session.SaveChanges();

                    // SAMPLE: use_all_your_own_sql
                    var user = session.Query<User>("select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'").Single();
                    // ENDSAMPLE
                    user.LastName.ShouldBe("Miller");
                    user.Id.ShouldBe(u.Id);
                }
            }
        }

        [Fact]
        public async Task query_with_select_in_query_async()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();

                // SAMPLE: using-queryasync
                using (var session = store.OpenSession())
                {
                    var u = new User { FirstName = "Jeremy", LastName = "Miller" };
                    session.Store(u);
                    session.SaveChanges();

                    var users = await session.QueryAsync<User>("select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'");
                    var user = users.Single();

                    user.LastName.ShouldBe("Miller");
                    user.Id.ShouldBe(u.Id);
                }
                // ENDSAMPLE

            }
        }

        [Fact]
        // SAMPLE: query_with_only_the_where_clause
        public void query_for_single_document()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentStore>().OpenSession())
                {
                    var u = new User { FirstName = "Jeremy", LastName = "Miller" };
                    session.Store(u);
                    session.SaveChanges();

                    var user = session.Query<User>("where data ->> 'FirstName' = 'Jeremy'").Single();
                    user.LastName.ShouldBe("Miller");
                    user.Id.ShouldBe(u.Id);
                }
            }
        }
        // ENDSAMPLE

        [Fact]
        public void query_for_multiple_documents()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();
                store.Advanced.Clean.CompletelyRemoveAll();

                using (var session = store.OpenSession())
                {
                    session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                    session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                    session.Store(new User { FirstName = "Max", LastName = "Miller" });
                    session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                    session.SaveChanges();

                    var firstnames = session.Query<User>("where data ->> 'LastName' = 'Miller'").OrderBy(x => x.FirstName)
                        .Select(x => x.FirstName).ToArray();
                        
                    firstnames.Length.ShouldBe(3);
                    firstnames[0].ShouldBe("Jeremy");
                    firstnames[1].ShouldBe("Lindsey");
                    firstnames[2].ShouldBe("Max");
                }
            }
        }

        [Fact]
        public void query_by_one_parameter()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentStore>().OpenSession())
                {
                    session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                    session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                    session.Store(new User { FirstName = "Max", LastName = "Miller" });
                    session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                    session.SaveChanges();

                    var firstnames = session.Query<User>("where data ->> 'LastName' = ?", "Miller").OrderBy(x => x.FirstName)
                        .Select(x => x.FirstName).ToArray();

                    firstnames.Length.ShouldBe(3);
                    firstnames[0].ShouldBe("Jeremy");
                    firstnames[1].ShouldBe("Lindsey");
                    firstnames[2].ShouldBe("Max");
                }
            }
        }

        [Fact]
        public void query_by_two_parameters()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentStore>().OpenSession())
                {
                    session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                    session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                    session.Store(new User { FirstName = "Max", LastName = "Miller" });
                    session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                    session.SaveChanges();
                    // SAMPLE: using_parameterized_sql
                    var user =
                        session.Query<User>("where data ->> 'FirstName' = ? and data ->> 'LastName' = ?", "Jeremy",
                            "Miller")
                            .Single();
                    // ENDSAMPLE

                    user.ShouldNotBeNull();
                }
            }
        }


        [Fact]
        public void query_for_multiple_documents_with_ordering()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentStore>().OpenSession())
                {
                    session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                    session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                    session.Store(new User { FirstName = "Max", LastName = "Miller" });
                    session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                    session.SaveChanges();

                    var firstnames = session.Query<User>("where data ->> 'LastName' = 'Miller' order by data ->> 'FirstName'")
                        .Select(x => x.FirstName).ToArray();

                    firstnames.Length.ShouldBe(3);
                    firstnames[0].ShouldBe("Jeremy");
                    firstnames[1].ShouldBe("Lindsey");
                    firstnames[2].ShouldBe("Max");
                }
            }
        }
    }
}