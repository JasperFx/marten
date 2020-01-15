namespace Marten.Testing.Examples
{
    public class DDLCustomization
    {
        public void use_create_if_none()
        {
            // SAMPLE: customizing_table_creation
            var store = DocumentStore.For(_ =>
            {
                _.DdlRules.TableCreation = CreationStyle.CreateIfNotExists;

                // or the default

                _.DdlRules.TableCreation = CreationStyle.DropThenCreate;
            });
            // ENDSAMPLE
        }

        public void use_security_definer()
        {
            // SAMPLE: customizing_upsert_rights
            var store = DocumentStore.For(_ =>
            {
                // Opt into SECURITY DEFINER permissions
                _.DdlRules.UpsertRights = SecurityRights.Definer;

                // The default SECURITY INVOKER permissions
                _.DdlRules.UpsertRights = SecurityRights.Invoker;
            });
            // ENDSAMPLE
        }

        public void configure_role()
        {
            // SAMPLE: customizing_role
            var store = DocumentStore.For(_ =>
            {
                _.DdlRules.Role = "ROLE1";
            });

            // ENDSAMPLE
        }

        public void read_templates()
        {
            // SAMPLE: using_ddl_templates
            var store = DocumentStore.For(_ =>
            {
                // let's say that you have template files in a
                // "templates" directory under the root of your
                // application
                _.DdlRules.ReadTemplates("templates");

                // Or just sweep the base directory of your application
                _.DdlRules.ReadTemplates();
            });
            // ENDSAMPLE
        }

        public void specify_template_in_fi()
        {
            // SAMPLE: configure_ddl_template_by_fi
            var store = DocumentStore.For(_ =>
            {
                _.Schema.For<User>().DdlTemplate("readonly");
            });
            // ENDSAMPLE
        }
    }
}
