using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class ConfiguringDatabaseSchemaName
    {
        internal static void configure()
        {
            #region sample_setting_database_schema_name

            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");
                opts.DatabaseSchemaName = "other";
            });

            #endregion
        }

        internal static void configure_schema_by_document_type()
        {
            #region sample_configure_schema_by_document_type

            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");
                opts.DatabaseSchemaName = "other";

                // This would take precedence for the
                // User document type storage
                opts.Schema.For<User>()
                    .DatabaseSchemaName("users");
            });

            #endregion
        }
    }
}
