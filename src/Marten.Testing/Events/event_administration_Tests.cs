using System;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Services;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("DefaultSchema")]
    public class event_administration_Tests : IntegratedFixture
    {
        public event_administration_Tests()
        {
            theStore.Schema.EnsureStorageExists(typeof (EventStream));

            theStore.EventStore.InitializeEventStoreInDatabase(true);
        }

        [Fact]
        public void has_the_event_tables()
        {
            var schema = theStore.Schema;
            var tableNames = schema.DbObjects.SchemaTables();
            tableNames.ShouldContain("public.mt_streams");
            tableNames.ShouldContain("public.mt_events");
            tableNames.ShouldContain("public.mt_modules");
            tableNames.ShouldContain("public.mt_projections");
        }

        [Fact]
        public void has_the_commands_for_appending_events()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("public.mt_append_event");
            functions.ShouldContain("public.mt_load_projection_body");
        }


        [Fact]
        public void has_the_command_for_transforming_events()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("public.mt_apply_transform");
        }

        [Fact]
        public void has_the_command_for_applying_aggregation()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("public.mt_apply_aggregation");
        }

        [Fact]
        public void has_the_command_for_starting_a_new_aggregate()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("public.mt_start_aggregation");
        }

        [Fact]
        public void loads_the_mt_transform_module()
        {
            using (var runner = theStore.Advanced.OpenConnection())
            {
                var loadedModules = runner.GetStringList("select name from public.mt_modules");
                loadedModules.ShouldContain("mt_transforms");
            }
        }

        [Fact]
        public void loads_the_initialize_projections_function()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("public.mt_initialize_projections");
        }

        [Fact]
        public void loads_the_get_projection_usage_function()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("public.mt_get_projection_usage");
        }


        [Fact]
        public void load_projections()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");

            theStore.EventStore.LoadProjections(directory);


            using (var runner = theStore.Advanced.OpenConnection())
            {
                var list = runner.GetStringList("select name from public.mt_projections order by name");


                list.ShouldContain("fake_aggregate");
                list.ShouldContain("location");
                list.ShouldContain("party");
            }
        }


        // Turning this off just for the moment
        //[Fact]
        public void load_projections_and_initialize()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");


            theStore.EventStore.LoadProjections(directory);
            var usages = theStore.EventStore.InitializeEventStoreInDatabase(true);

            usages.Where(x => x.name == "location")
                .Select(x => x.event_name)
                .ShouldHaveTheSameElementsAs("members_joined", "members_departed");
            usages.Where(x => x.name == "fake_aggregate")
                .Select(x => x.event_name)
                .ShouldHaveTheSameElementsAs("event_a", "event_b", "event_c", "event_d");


            /*

            Projection fake_aggregate (snapshot) for Event a_name executed inline
Projection fake_aggregate (snapshot) for Event b_name executed inline
Projection fake_aggregate (snapshot) for Event c_name executed inline
Projection fake_aggregate (snapshot) for Event d_name executed inline
Projection location (transform) for Event members_joined executed inline
Projection location (transform) for Event members_departed executed inline
Projection party (snapshot) for Event members_joined executed inline
Projection party (snapshot) for Event quest_started executed inline
    */
        }

        [Fact]
        public void initialize_can_run_without_blowing_up()
        {
            theStore.EventStore.InitializeEventStoreInDatabase();
        }
    }

    public class event_administration_in_different_store_schema_Tests : IntegratedFixture
    {
        public event_administration_in_different_store_schema_Tests()
        {
            var schema = theStore.Schema;
            schema.StoreOptions.DatabaseSchemaName = "other";
            schema.EnsureStorageExists(typeof (EventStream));

            theStore.EventStore.InitializeEventStoreInDatabase(true);
        }

        [Fact]
        public void has_the_event_tables()
        {
            var schema = theStore.Schema;
            var tableNames = schema.DbObjects.SchemaTables();
            tableNames.ShouldContain("other.mt_streams");
            tableNames.ShouldContain("other.mt_events");
            tableNames.ShouldContain("other.mt_modules");
            tableNames.ShouldContain("other.mt_projections");
        }

        [Fact]
        public void has_the_commands_for_appending_events()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("other.mt_append_event");
            functions.ShouldContain("other.mt_load_projection_body");
        }


        [Fact]
        public void has_the_command_for_transforming_events()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("other.mt_apply_transform");
        }

        [Fact]
        public void has_the_command_for_applying_aggregation()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("other.mt_apply_aggregation");
        }

        [Fact]
        public void has_the_command_for_starting_a_new_aggregate()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("other.mt_start_aggregation");
        }

        [Fact]
        public void loads_the_mt_transform_module()
        {
            using (var runner = theStore.Advanced.OpenConnection())
            {
                var loadedModules = runner.GetStringList("select name from other.mt_modules");
                loadedModules.ShouldContain("mt_transforms");
            }
        }

        [Fact]
        public void loads_the_initialize_projections_function()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("other.mt_initialize_projections");
        }

        [Fact]
        public void loads_the_get_projection_usage_function()
        {
            var schema = theStore.Schema;

            var functions = schema.DbObjects.SchemaFunctionNames();
            functions.ShouldContain("other.mt_get_projection_usage");
        }

        [Fact]
        public void load_projections()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");

            theStore.EventStore.LoadProjections(directory);

            using (var runner = theStore.Advanced.OpenConnection())
            {
                var list = runner.GetStringList("select name from other.mt_projections order by name");

                list.ShouldContain("fake_aggregate");
                list.ShouldContain("location");
                list.ShouldContain("party");
            }
        }

        // Turning this off just for the moment
        //[Fact]
        public void load_projections_and_initialize()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");

            theStore.EventStore.LoadProjections(directory);
            var usages = theStore.EventStore.InitializeEventStoreInDatabase(true);

            usages.Where(x => x.name == "location")
                .Select(x => x.event_name)
                .ShouldHaveTheSameElementsAs("members_joined", "members_departed");
            usages.Where(x => x.name == "fake_aggregate")
                .Select(x => x.event_name)
                .ShouldHaveTheSameElementsAs("event_a", "event_b", "event_c", "event_d");


        }

        [Fact]
        public void initialize_can_run_without_blowing_up()
        {
            theStore.EventStore.InitializeEventStoreInDatabase();
        }
    }
}