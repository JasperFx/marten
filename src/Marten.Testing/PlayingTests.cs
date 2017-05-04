using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Events.Projections;
using Marten.Util;
using StructureMap;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing
{
    public class PlayingTests
    {
        private readonly ITestOutputHelper _output;

        public PlayingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void read_as_text_reader()
        {
            using (var store = TestingDocumentStore.Basic())
            {
                var target = Target.Random(true);

                using (var session = store.OpenSession())
                {
                    session.Store(target);
                    session.SaveChanges();
                }
                
                using (var conn = store.Tenants.Default.OpenConnection())
                {
                    var json = conn.Execute(cmd =>
                    {
                        using (var reader = cmd.Sql("select data from mt_doc_target").ExecuteReader())
                        {
                            reader.Read();
                            return reader.GetTextReader(0).ReadToEnd();
                        }
                    });

                    Console.WriteLine(json);
                }
            }
        }

        [Fact]
        public void can_generate_a_computed_index()
        {
            using (var store = TestingDocumentStore.Basic())
            {
                store.Tenants.Default.EnsureStorageExists(typeof(User));

                var mapping = store.Storage.MappingFor(typeof(User));
                var sql = mapping.As<DocumentMapping>().FieldFor(nameof(User.UserName)).As<JsonLocatorField>().ToComputedIndex(mapping.Table)
                    .Replace("d.data", "data");

                using (var conn = store.Tenants.Default.OpenConnection())
                {
                    conn.Execute(cmd => cmd.Sql(sql).ExecuteNonQuery());
                }

                using (var session = store.OpenSession())
                {
                    var query =
                        session.Query<User>()
                            .Where(x => x.UserName == "hank")
                            .ToCommand(FetchType.FetchMany)
                            .CommandText;

                    _output.WriteLine(query);

                    var plan = session.Query<User>().Where(x => x.UserName == "hank").Explain();
                    _output.WriteLine(plan.ToString());
                }
            }
        }

        [Fact]
        public void fetch_index_definitions()
        {
            using (var store = TestingDocumentStore.For(_ =>
            {
                _.Schema.For<User>().Duplicate(x => x.UserName);
            }))
            {
                store.BulkInsert(new User[] {new User {UserName = "foo"}, new User { UserName = "bar" }, });
            }
        }

        [Fact]
        public void try_some_linq_queries()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();
                store.BulkInsert(Target.GenerateRandomData(200).ToArray());

                using (var session = store.LightweightSession())
                {
                    Debug.WriteLine(session.Query<Target>().Where(x => x.Double > 1.0).Count());
                    Debug.WriteLine(session.Query<Target>().Where(x => x.Double > 1.0 && x.Double < 33).Count());
                }
            }
        }

        public void try_out_select()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var store = container.GetInstance<IDocumentStore>();
                store.Advanced.Clean.CompletelyRemoveAll();
                store.BulkInsert(Target.GenerateRandomData(200).ToArray());

                using (var session = store.QuerySession())
                {
                    session.Query<Target>().Select(x => x.Double).ToArray();
                }

            }
        }
    }


}