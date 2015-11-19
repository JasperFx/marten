using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FubuCore;
using Marten.Schema;
using Shouldly;

namespace Marten.Testing.Events
{
    public class event_administration_Tests : DocumentSessionFixture
    {
        public event_administration_Tests()
        {
            var events = theContainer.GetInstance<Marten.Events.EventStore>();

            events.RebuildEventStoreSchema();
        }

        public void has_the_event_tables()
        {
            var schema = theContainer.GetInstance<IDocumentSchema>();
            var tableNames = schema.SchemaTableNames();
            tableNames.ShouldContain("mt_streams");
            tableNames.ShouldContain("mt_events");
            tableNames.ShouldContain("mt_modules");
            tableNames.ShouldContain("mt_projections");
        }

        public void has_the_commands_for_appending_events()
        {
            var schema = theContainer.GetInstance<IDocumentSchema>();

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain("mt_append_event");
            functions.ShouldContain("mt_load_projection_body");
        }


        public void has_the_command_for_transforming_events()
        {
            var schema = theContainer.GetInstance<IDocumentSchema>();

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain("mt_apply_transform");
        }

        public void has_the_command_for_applying_aggregation()
        {
            var schema = theContainer.GetInstance<IDocumentSchema>();

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain("mt_apply_aggregation");
        }

        public void loads_the_mt_transform_module()
        {
            var runner = theContainer.GetInstance<ICommandRunner>();

            var loadedModules = runner.GetStringList("select name from mt_modules");
            loadedModules.ShouldContain("mt_transforms");
        }

        public void loads_the_initialize_projections_function()
        {
            var schema = theContainer.GetInstance<IDocumentSchema>();

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain("mt_initialize_projections");
        }

        public void loads_the_get_projection_usage_function()
        {
            var schema = theContainer.GetInstance<IDocumentSchema>();

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain("mt_get_projection_usage");
        }
        

        public void load_projections()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");



            var events = theContainer.GetInstance<Marten.Events.EventStore>();

            events.RebuildEventStoreSchema();

            events.Administration.LoadProjections(directory);


            var runner = theContainer.GetInstance<ICommandRunner>();
            var list = runner.GetStringList("select name from mt_projections order by name");


            list.ShouldContain("fake_aggregate");
            list.ShouldContain("location");
            list.ShouldContain("party");
        }

        public void load_projections_and_initialize()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");



            var events = theContainer.GetInstance<Marten.Events.EventStore>();

            events.RebuildEventStoreSchema();

            events.Administration.LoadProjections(directory);
            var usages = events.Administration.InitializeEventStoreInDatabase();

            usages.Where(x => x.name == "location").Select(x => x.event_name).ShouldHaveTheSameElementsAs("members_joined", "members_departed");
            usages.Where(x => x.name == "fake_aggregate").Select(x => x.event_name).ShouldHaveTheSameElementsAs("event_a", "event_b", "event_c", "event_d");

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

        public void initialize_can_run_without_blowing_up()
        {
            var events = theContainer.GetInstance<Marten.Events.EventStore>();
            events.Administration.InitializeEventStoreInDatabase();
        }
    }
}