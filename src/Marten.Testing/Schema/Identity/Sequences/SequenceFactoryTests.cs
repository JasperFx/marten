using Baseline;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    [Collection("DefaultSchema")]
    public class SequenceFactoryTests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;

        public SequenceFactoryTests()
        {
            _schema = theStore.Schema;

            _schema.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            _schema.DbObjects.SchemaFunctionNames().ShouldContain("public.mt_get_next_hi");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            _schema.DbObjects.SchemaFunctionNames().ShouldContain("public.mt_get_next_hi");
        }


    }

    public class SequenceFactory_patch_generation : IntegratedFixture
    {
        [Fact]
        public void generate_drop_part_of_patch()
        {
            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists public.mt_hilo cascade;");
        }

        [Fact]
        public void generate_drop_part_of_patch_different_schema()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");
            var patch = theStore.Schema.ToPatch();

            patch.RollbackDDL.ShouldContain("drop table if exists other.mt_hilo cascade;");
        }

        [Fact]
        public void grant_generation()
        {
            StoreOptions(_ =>
            {
                _.DdlRules.Grants.Add("foo");
                _.DdlRules.Grants.Add("bar");

                _.DatabaseSchemaName = "other";
            });

            var patch = theStore.Schema.ToPatch();

            patch.UpdateDDL.ShouldContain("GRANT SELECT ON TABLE other.mt_hilo TO \"foo\";");
            patch.UpdateDDL.ShouldContain("GRANT UPDATE ON TABLE other.mt_hilo TO \"foo\";");
            patch.UpdateDDL.ShouldContain("GRANT INSERT ON TABLE other.mt_hilo TO \"foo\";");
            patch.UpdateDDL.ShouldContain("GRANT EXECUTE ON FUNCTION other.mt_get_next_hi(varchar) TO \"foo\";");


            patch.UpdateDDL.ShouldContain("GRANT SELECT ON TABLE other.mt_hilo TO \"bar\";");
            patch.UpdateDDL.ShouldContain("GRANT UPDATE ON TABLE other.mt_hilo TO \"bar\";");
            patch.UpdateDDL.ShouldContain("GRANT INSERT ON TABLE other.mt_hilo TO \"bar\";");
            patch.UpdateDDL.ShouldContain("GRANT EXECUTE ON FUNCTION other.mt_get_next_hi(varchar) TO \"bar\";");
        }

        [Fact]
        public void should_have_grants_with_the_sequence_factory_in_to_ddl()
        {
            StoreOptions(_ =>
            {
                _.DdlRules.Grants.Add("foo");
                _.DdlRules.Grants.Add("bar");

                _.DatabaseSchemaName = "other";
            });

            var sql = theStore.Schema.ToDDL();

            sql.ShouldContain("GRANT SELECT ON TABLE other.mt_hilo TO \"foo\";");
            sql.ShouldContain("GRANT UPDATE ON TABLE other.mt_hilo TO \"foo\";");
            sql.ShouldContain("GRANT INSERT ON TABLE other.mt_hilo TO \"foo\";");
            sql.ShouldContain("GRANT EXECUTE ON FUNCTION other.mt_get_next_hi(varchar) TO \"foo\";");


            sql.ShouldContain("GRANT SELECT ON TABLE other.mt_hilo TO \"bar\";");
            sql.ShouldContain("GRANT UPDATE ON TABLE other.mt_hilo TO \"bar\";");
            sql.ShouldContain("GRANT INSERT ON TABLE other.mt_hilo TO \"bar\";");
            sql.ShouldContain("GRANT EXECUTE ON FUNCTION other.mt_get_next_hi(varchar) TO \"bar\";");
        }
    }

    public class SequenceFactoryOnOtherDatabaseSchemaTests : IntegratedFixture
    {
        public SequenceFactoryOnOtherDatabaseSchemaTests()
        {
            StoreOptions(x => x.DatabaseSchemaName = "seq_other");

            theStore.Schema.Sequences.Hilo(typeof(Target), new HiloSettings())
                .ShouldBeOfType<HiloSequence>();
        }

        [Fact]
        public void can_create_table_on_fly_if_necessary()
        {
            theStore.Schema.DbObjects.SchemaTables().ShouldContain("seq_other.mt_hilo");
        }

        [Fact]
        public void can_create_function_on_fly_if_necessary()
        {
            theStore.Schema.DbObjects.SchemaFunctionNames().ShouldContain("seq_other.mt_get_next_hi");
        }
    }
}