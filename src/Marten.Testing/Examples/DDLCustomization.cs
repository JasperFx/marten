using Marten.Testing.Documents;
using Weasel.Core;

namespace Marten.Testing.Examples;

public class DDLCustomization
{
    public void use_create_if_none()
    {
        #region sample_customizing_table_creation
        var store = DocumentStore.For(_ =>
        {
            _.Advanced.Migrator.TableCreation = CreationStyle.CreateIfNotExists;

            // or the default

            _.Advanced.Migrator.TableCreation = CreationStyle.DropThenCreate;
        });
        #endregion
    }

    public void use_security_definer()
    {
        #region sample_customizing_upsert_rights
        var store = DocumentStore.For(_ =>
        {
            // Opt into SECURITY DEFINER permissions
            _.Advanced.Migrator.UpsertRights = SecurityRights.Definer;

            // The default SECURITY INVOKER permissions
            _.Advanced.Migrator.UpsertRights = SecurityRights.Invoker;
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

    public void read_templates()
    {
        #region sample_using_ddl_templates
        var store = DocumentStore.For(_ =>
        {
            // let's say that you have template files in a
            // "templates" directory under the root of your
            // application
            _.Advanced.Migrator.ReadTemplatesAsync("templates");

        });
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
