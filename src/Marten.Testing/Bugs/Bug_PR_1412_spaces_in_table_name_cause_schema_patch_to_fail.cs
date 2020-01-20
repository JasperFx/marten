using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_PR_1412_spaces_in_table_name_cause_schema_patch_to_fail : IntegratedFixture
    {

        internal class testSchema : IFeatureSchema
        {
            public ISchemaObject[] Objects => SchemaObjects().ToArray();

            public Type StorageType => GetType();

            public string Identifier { get; set; }

            public IEnumerable<Type> DependentTypes()
            {
                return new List<Type>();
            }

            public bool IsActive(StoreOptions options)
            {
                return true;
            }

            public void WritePermissions(DdlRules rules, StringWriter writer)
            {
                // No permissions!
            }

            protected virtual IEnumerable<ISchemaObject> SchemaObjects()
            {
                List<ISchemaObject> objects = new List<ISchemaObject>();
                return objects;
            }

        }

        internal class spaceAfterTableNameSchema: testSchema
        {
            protected override IEnumerable<ISchemaObject> SchemaObjects()
            {
                var table = new Table(new DbObjectName("test_space_after"));
                table.AddColumn("space_after ", "int");
                return new List<ISchemaObject>
                {
                        table
                };
            }
        }
        [Fact]
        public void space_after_table_name_does_not_cause_exception_on_update()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceAfterTableNameSchema());
            });
            Should.NotThrow(() =>
            {
                // Calling twice because create can succeed, but update fails
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            });
        }
        internal class spaceBeforeTableNameSchema: testSchema
        {
            protected override IEnumerable<ISchemaObject> SchemaObjects()
            {
                var table = new Table(new DbObjectName("test_space_before"));
                table.AddColumn(" space_before", "int");
                return new List<ISchemaObject>
                {
                    table
                };
            }
        }
        [Fact]
        public void space_before_table_name_does_not_cause_exception_on_update()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceBeforeTableNameSchema());
            });
            Should.NotThrow(() =>
            {
                // Calling twice because create can succeed, but update fails
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            });
        }
        internal class spaceInNameSchema: testSchema
        {
            protected override IEnumerable<ISchemaObject> SchemaObjects()
            {
                var table = new Table(new DbObjectName("test_space_in"));
                table.AddColumn("space inname", "int");
                return new List<ISchemaObject>
                {
                    table
                };
            }
        }
        [Fact]
        public void space_in_table_name_does_not_cause_exception_on_update()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceInNameSchema());
            });
            Should.NotThrow(() =>
            {
                // Calling twice because create can succeed, but update fails
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            });
        }
    }
}
