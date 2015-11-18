using System;
using System.Collections.Generic;
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
    }
}