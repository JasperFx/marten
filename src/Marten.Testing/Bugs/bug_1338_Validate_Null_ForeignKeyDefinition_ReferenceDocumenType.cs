using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1338_Validate_Null_ForeignKeyDefinition_ReferenceDocumenType : IntegratedFixture
    {
        [Fact]
        public void StorageFeatures_AllActiveFeatures_Should_Not_Throw_With_ExternalForeignKeyDefinitions()
        {
            CreateExternalTableForTesting();

            StoreOptions(_ =>
                         {
                             _.Schema.For<ClassWithExternalForeignKey>().ForeignKey(x => x.ForiegnId, _.DatabaseSchemaName, "external_table", "id");
                         });

            theStore.Storage.AllActiveFeatures(theStore.Tenancy.Default).All(x => x != null).ShouldBeTrue();
        }

        [Fact]
        public void UnitOfWork_GetTypeDependencies_Should_Not_Throw_With_ExternalForeignKeyDefinitions()
        {
            CreateExternalTableForTesting();

            StoreOptions(_ =>
                         {
                             _.Schema.For<ClassWithExternalForeignKey>().ForeignKey(x => x.ForiegnId, _.DatabaseSchemaName, "external_table", "id");
                         });

            // Inserting a new document will force a call to:
            //  UnitOfWork.ApplyChanges()
            //  UnitOfWork.buildChangeSet()
            //  UnitOfWork.determineChanges()
            //  UnitOfWork.shouldSort()
            //  and finally, the function that we want to regression test"
            //  UnitOfWork.GetTypeDependencies(ClassWithExternalForeignKey) 
            using (var session = theStore.LightweightSession())
            {
                session.Insert(new ClassWithExternalForeignKey{Id  = 1, ForiegnId = 1});
                session.SaveChanges();
            }
        }

        private static void CreateExternalTableForTesting()
        {
            const string dropSql = "DROP TABLE IF EXISTS public.external_table CASCADE;";
            const string createSql =
@"CREATE TABLE public.external_table (
    id integer,
    CONSTRAINT ""external_table_pkey"" PRIMARY KEY (id)
);";
            const string insertSql = "INSERT INTO public.external_table VALUES (1);";

            using (var dbConn = new NpgsqlConnection(ConnectionSource.ConnectionString))
            {
                dbConn.Open();

                NpgsqlCommand cmd;
                using (cmd = new NpgsqlCommand(dropSql, dbConn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (cmd = new NpgsqlCommand(createSql, dbConn))
                {
                    cmd.ExecuteNonQuery();
                }

                using (cmd = new NpgsqlCommand(insertSql, dbConn))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }

    }

    public class FakeExternalTable: FeatureSchemaBase
    {
        public FakeExternalTable(StoreOptions options) : base("fake_external_table", options)
        {
        }

        protected override IEnumerable<ISchemaObject> schemaObjects()
        {
            var table = new Table(new DbObjectName(Options.DatabaseSchemaName, "external_table"));
            table.AddColumn("id", "integer");

            yield return table;
        }
    }

    public class ClassWithExternalForeignKey
    {
        public int Id { get; set; }
        public int ForiegnId { get; set; }
    }
}
