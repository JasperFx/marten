using System.Diagnostics;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class ExportingDDL
    {
        #region sample_export-ddl
        public void export_ddl()
        {
            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // If you are depending upon attributes for customization,
                // you have to help DocumentStore "know" what the document types
                // are
                _.Schema.For<User>();
                _.Schema.For<Company>();
                _.Schema.For<Issue>();
            });

            // Export the SQL to a file
            store.Schema.WriteDDL("my_database.sql");

            // Or instead, write a separate sql script
            // to the named directory
            // for each type of document
            store.Schema.WriteDDLByType("sql");

            // or just see it
            var sql = store.Schema.ToDDL();
            Debug.WriteLine(sql);
        }

        #endregion sample_export-ddl
    }
}
