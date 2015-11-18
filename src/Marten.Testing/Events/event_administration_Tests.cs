using System;
using System.Collections.Generic;
using FubuCore;
using Marten.Schema;
using Shouldly;

namespace Marten.Testing.Events
{
    public class event_administration_Tests : DocumentSessionFixture
    {

        public void rebuild_event_store_schema()
        {

            var events = theContainer.GetInstance<Marten.Events.EventStore>();

            events.RebuildEventStoreSchema();

            var runner = theContainer.GetInstance<ICommandRunner>();



            var schema = theContainer.GetInstance<IDocumentSchema>();
            var tableNames = schema.SchemaTableNames();
            tableNames.ShouldContain("mt_streams");
            tableNames.ShouldContain("mt_events");
            tableNames.ShouldContain("mt_modules");
            tableNames.ShouldContain("mt_projections");

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain("mt_append_event");
            functions.ShouldContain("mt_load_projection_body");

            var loadedModules = runner.GetStringList("select name from mt_modules");
            loadedModules.ShouldContain("mt_transforms");
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