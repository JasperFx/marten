using System.Diagnostics;
using Marten.Testing.Documents;
using StructureMap;

namespace Marten.Testing.Examples
{
    public class ExportingDDL
    {
        // SAMPLE: export-ddl
        public void export_ddl()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // include any MartenRegistry's you are using
                _.Schema.Include<MyMartenRegistry>();

                // If you are depending upon attributes for customization,
                // you have to help DocumentStore "know" what the document types
                // are
                _.Schema.For<User>();
                _.Schema.For<Company>();
                _.Schema.For<Issue>();
            });

            // Export the SQL to a file
            store.Schema.WriteDDL("my_database.sql");

            // or just see it
            var sql = store.Schema.ToDDL();
            Debug.WriteLine(sql);
        } 
        // ENDSAMPLE
    }
}