using System;
using System.Collections.Generic;
using FubuCore;
using Shouldly;

namespace Marten.Testing.Events
{
    public class event_administration_Tests : DocumentSessionFixture
    {
        public void load_projections()
        {
            var directory =
                AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory().AppendPath("Events");



            var events = theContainer.GetInstance<Marten.Events.EventStore>();

            events.RebuildEventStoreSchema();

            events.Administration.LoadProjections(directory);


            var runner = theContainer.GetInstance<ICommandRunner>();
            var list = new List<string>();

            runner.Execute(conn =>
            {
                var cmd = conn.CreateCommand();
                cmd.CommandText = "select name from mt_projections order by name";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(reader.GetString(0));
                    }

                    reader.Close();
                }
            });

            list.ShouldContain("fake_aggregate");
            list.ShouldContain("location");
            list.ShouldContain("party");
        }
    }
}