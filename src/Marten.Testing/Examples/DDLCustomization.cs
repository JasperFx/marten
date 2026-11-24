using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Testing.Examples;

public class DDLCustomization
{
    public void use_create_if_none()
    {
        #region sample_customizing_table_creation
        var store = DocumentStore.For(_ =>
        {
            // This is the default
            _.Advanced.Migrator.TableCreation = CreationStyle.CreateIfNotExists;

            // or, to drop and recreate the table on each schema migration
            _.Advanced.Migrator.TableCreation = CreationStyle.DropThenCreate;
        });
        #endregion
    }

    public void configure_role()
    {
        #region sample_customizing_role
        var store = DocumentStore.For(_ =>
        {
            _.Advanced.Migrator.Role = "ROLE1";
        });

        #endregion
    }

    public async Task read_templates()
    {
        #region sample_using_ddl_templates
        var options = new StoreOptions();
        options.Connection("some connection string");

        // let's say that you have template files in a
        // "templates" directory under the root of your
        // application. Reading them is asynchronous, so
        // read them before you build the store
        await options.Advanced.Migrator.ReadTemplatesAsync("templates");

        var store = new DocumentStore(options);
        #endregion
    }

    public void specify_template_in_fi()
    {
        #region sample_configure_ddl_template_by_fi
        var store = DocumentStore.For(_ =>
        {
            _.Schema.For<User>().DdlTemplate("readonly");
        });
        #endregion
    }
}

#region sample_configure_template_with_attribute
[DdlTemplate("readonly")]
public class AuditRecord
{
    public Guid Id { get; set; }
}
#endregion

#region sample_configure_template_with_configure_marten
public class LegacyInvoice
{
    public Guid Id { get; set; }

    // Marten calls a public static ConfigureMarten(DocumentMapping) method
    // on a document type when it builds that type's mapping
    public static void ConfigureMarten(DocumentMapping mapping)
    {
        mapping.DdlTemplate = "readonly";
    }
}
#endregion
