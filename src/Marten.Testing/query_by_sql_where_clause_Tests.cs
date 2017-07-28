using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing
{
    public class query_by_sql_where_clause_Tests : IntegratedFixture
    {


        [Fact]
        public void query_by_one_parameter()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();

                var firstnames =
                    session.Query<User>($"where data ->> '{session.ColumnName<User>(u => u.LastName)}' = ?", "Miller").OrderBy(x => x.FirstName)
                           .Select(x => x.FirstName).ToArray();

                firstnames.Length.ShouldBe(3);
                firstnames[0].ShouldBe("Jeremy");
                firstnames[1].ShouldBe("Lindsey");
                firstnames[2].ShouldBe("Max");
            }
        }

        [Fact]
        public void query_by_one_named_parameter()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();

                var firstnames =
                    session.Query<User>($"where data ->> '{session.ColumnName<User>(u => u.LastName)}' = :Name", new { Name = "Miller" }).OrderBy(x => x.FirstName)
                           .Select(x => x.FirstName).ToArray();

                firstnames.Length.ShouldBe(3);
                firstnames[0].ShouldBe("Jeremy");
                firstnames[1].ShouldBe("Lindsey");
                firstnames[2].ShouldBe("Max");
            }
        }

        [Fact]
        public void query_by_two_parameters()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();
                // SAMPLE: using_parameterized_sql
                var user =
                    session.Query<User>($"where data ->> '{theStore.Storage.ColumnName<User>(u => u.FirstName)}' = ? and data ->> '{session.ColumnName<User>(u => u.LastName)}' = ?", "Jeremy",
                               "Miller")
                           .Single();
                // ENDSAMPLE

                user.ShouldNotBeNull();
            }
        }

        // ENDSAMPLE

        // SAMPLE: query_by_two_named_parameters
        [Fact]
        public void query_by_two_named_parameters()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();
                var user =
                    session.Query<User>($"where data ->> '{theStore.Storage.ColumnName<User>(u => u.FirstName)}' = :FirstName and data ->> '{session.ColumnName<User>(u => u.LastName)}' = :LastName", new { FirstName = "Jeremy", LastName = "Miller" })
                           .Single();

                user.ShouldNotBeNull();
            }
        }
        // ENDSAMPLE

        [Fact]
        public void query_two_fields_by_one_named_parameter()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();
                var user =
                    session.Query<User>($"where data ->> '{session.ColumnName<User>(u => u.FirstName)}' = :Name or data ->> '{session.ColumnName<User>(u => u.LastName)}' = :Name", new { Name = "Jeremy" })
                           .Single();

                user.ShouldNotBeNull();
            }
        }

        [Fact]
        public void query_for_multiple_documents()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();

                var firstnames =
                    session.Query<User>($"where data ->> '{session.ColumnName<User>(u => u.LastName)}' = 'Miller'").OrderBy(x => x.FirstName)
                           .Select(x => x.FirstName).ToArray();

                firstnames.Length.ShouldBe(3);
                firstnames[0].ShouldBe("Jeremy");
                firstnames[1].ShouldBe("Lindsey");
                firstnames[2].ShouldBe("Max");
            }
        }


        [Fact]
        public void query_for_multiple_documents_with_ordering()
        {
            using (var session = theStore.OpenSession())
            {
                session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
                session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
                session.Store(new User { FirstName = "Max", LastName = "Miller" });
                session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
                session.SaveChanges();

                var firstnames =
                    session.Query<User>($"where data ->> '{session.ColumnName<User>(u => u.LastName)}' = 'Miller' order by data ->> '{session.ColumnName<User>(u => u.FirstName)}ttes'")
                           .Select(x => x.FirstName).ToArray();

                firstnames.Length.ShouldBe(3);
                firstnames[0].ShouldBe("Jeremy");
                firstnames[1].ShouldBe("Lindsey");
                firstnames[2].ShouldBe("Max");
            }
        }


        // SAMPLE: query_with_only_the_where_clause
        [Fact]
        public void query_for_single_document()
        {
            using (var session = theStore.OpenSession())
            {
                var u = new User { FirstName = "Jeremy", LastName = "Miller" };
                session.Store(u);
                session.SaveChanges();

                var user = session.Query<User>($"where data ->> '{theStore.Storage.ColumnName<User>(z => z.FirstName)}' = 'Jeremy'").Single();
                user.LastName.ShouldBe("Miller");
                user.Id.ShouldBe(u.Id);
            }
        }
        // ENDSAMPLE

        [Fact]
        public void query_with_select_in_query()
        {
            using (var session = theStore.OpenSession())
            {
                var u = new User { FirstName = "Jeremy", LastName = "Miller" };
                session.Store(u);
                session.SaveChanges();

                // SAMPLE: use_all_your_own_sql
                var user =
                    session.Query<User>($"select data from mt_doc_user where data ->> '{theStore.Storage.ColumnName<User>(z => z.FirstName)}' = 'Jeremy'")
                           .Single();
                // ENDSAMPLE
                user.LastName.ShouldBe("Miller");
                user.Id.ShouldBe(u.Id);
            }
        }

        [Fact]
        public async Task query_with_select_in_query_async()
        {
            using (var session = theStore.OpenSession())
            {
                var u = new User { FirstName = "Jeremy", LastName = "Miller" };
                session.Store(u);
                session.SaveChanges();

                var users =
                    await
                        session.QueryAsync<User>(
                                   $"select data from mt_doc_user where data ->> '{theStore.Storage.ColumnName<User>(z => z.FirstName)}' = 'Jeremy'")
                               .ConfigureAwait(false);
                var user = users.Single();

                user.LastName.ShouldBe("Miller");
                user.Id.ShouldBe(u.Id);
            }
            // ENDSAMPLE
        }
    }
}