using Marten.Testing.Documents;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Schema
{
    public class add_origin_Tests
    {     
        //[Fact] -- hiccups on CI because of having the assembly version in place
        public void origin_is_added_to_tables()
        {
            var user1 = new User { FirstName = "Jeremy" };
            var user2 = new User { FirstName = "Max" };
            var user3 = new User { FirstName = "Declan" };

            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {                
                store.Advanced.Clean.CompletelyRemoveAll();

                store.BulkInsert(new User[] { user1, user2, user3 });
            }

            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            using (var session = store.QuerySession())
            using (var cmd = session.Connection.CreateCommand())
            {
                var mapping = store.Schema.MappingFor(typeof(User));

                cmd.CommandText = "SELECT description from pg_description " +
                                  "join pg_class on pg_description.objoid = pg_class.oid where relname = :name";
                cmd.AddNamedParameter("name", mapping.Table.Name);

                var result = (string)cmd.ExecuteScalar();  
                Assert.NotNull(result);              
                Assert.Contains(typeof(IDocumentStore).AssemblyQualifiedName, result);
            }
        }
    }
}