using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_PR_1412_spaces_in_table_name_cause_schema_patch_to_fail: BugIntegrationContext
    {
        [Fact]
        public async Task space_after_table_name_does_not_cause_exception_on_update()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceAfterTableNameSchema());
            });
            await Should.NotThrowAsync(async () =>
            {
                await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                await theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        [Fact]
        public async Task space_before_table_name_does_not_cause_exception_on_update()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceBeforeTableNameSchema());
            });
            await Should.NotThrowAsync(async () =>
            {
                await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                await theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        [Fact]
        public async Task space_in_table_name_does_not_cause_exception_on_update()
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
                _.Storage.Add(new spaceInNameSchema());
            });
            await Should.NotThrowAsync(async () =>
            {
                await theStore.Schema.ApplyAllConfiguredChangesToDatabase();
                await theStore.Schema.AssertDatabaseMatchesConfiguration();
            });
        }

        internal class testSchema: IFeatureSchema
        {
            public ISchemaObject[] Objects => SchemaObjects().ToArray();

            public Type StorageType => GetType();

            public string Identifier { get; set; }

            public IEnumerable<Type> DependentTypes()
            {
                return new List<Type>();
            }

            public void WritePermissions(DdlRules rules, TextWriter writer)
            {
                // No permissions!
            }

            public bool IsActive(StoreOptions options)
            {
                return true;
            }

            protected virtual IEnumerable<ISchemaObject> SchemaObjects()
            {
                var objects = new List<ISchemaObject>();
                return objects;
            }
        }

        internal class spaceAfterTableNameSchema: testSchema
        {
            protected override IEnumerable<ISchemaObject> SchemaObjects()
            {
                var table = new Table(new DbObjectName(SchemaConstants.DefaultSchema,"test_space_after"));
                table.AddColumn("space_after ", "int");
                return new List<ISchemaObject> {table};
            }
        }

        internal class spaceBeforeTableNameSchema: testSchema
        {
            protected override IEnumerable<ISchemaObject> SchemaObjects()
            {
                var table = new Table(new DbObjectName(SchemaConstants.DefaultSchema,"test_space_before"));
                table.AddColumn(" space_before", "int");
                return new List<ISchemaObject> {table};
            }
        }

        internal class spaceInNameSchema: testSchema
        {
            protected override IEnumerable<ISchemaObject> SchemaObjects()
            {
                var table = new Table(new DbObjectName(SchemaConstants.DefaultSchema,"test_space_in"));
                table.AddColumn("space inname", "int");
                return new List<ISchemaObject> {table};
            }
        }
    }
}
