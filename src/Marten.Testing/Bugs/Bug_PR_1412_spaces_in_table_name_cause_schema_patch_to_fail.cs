using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
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
        public void space_after_table_name_does_not_cause_exception()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceAfterTableNameSchema());
            });
            try
            {
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception during space_after_table_name_does_not_cause_exception");
                Debug.WriteLine(e.ToString());
                Assert.False(true, e.ToString());
            }
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
        public void space_before_table_name_does_not_cause_exception()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceBeforeTableNameSchema());
            });
            try
            {
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception during space_before_table_name_does_not_cause_exception");
                Debug.WriteLine(e.ToString());
                Assert.False(true, e.ToString());
            }
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
        public void space_in_table_name_does_not_cause_exception()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceInNameSchema());
            });
            try
            {
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                theStore.Schema.ApplyAllConfiguredChangesToDatabase();
            }
            catch (Exception e)
            {
                Debug.WriteLine("Exception during space_in_table_name_does_not_cause_exception");
                Debug.WriteLine(e.ToString());
                Assert.False(true, e.ToString());
            }
        }
    }
}
