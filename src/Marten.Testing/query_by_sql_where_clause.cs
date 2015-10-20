using System.Linq;
using Marten.Testing.Documents;
using Shouldly;
using StructureMap;

namespace Marten.Testing
{
    public class query_by_sql_where_clause
    {
        public void query_for_single_document()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentSession>())
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

        public void query_for_multiple_documents()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentSession>())
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

        public void query_by_one_parameter()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentSession>())
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

        public void query_by_two_parameters()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentSession>())
                {
                    session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                    session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                    session.Store(new User { FirstName = "Max", LastName = "Miller" });
                    session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                    session.SaveChanges();

                    var user =
                        session.Query<User>("where data ->> 'FirstName' = ? and data ->> 'LastName' = ?", "Jeremy",
                            "Miller")
                            .Single();

                    user.ShouldNotBeNull();
                }
            }
        }


        public void query_for_multiple_documents_with_ordering()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container.GetInstance<IDocumentSession>())
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