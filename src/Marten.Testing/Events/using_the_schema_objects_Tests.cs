using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Testing.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Testing.Events
{
    [Collection("schema_objects")]
    public class using_the_schema_objects_Tests : OneOffConfigurationsContext
    {
        [Fact]
        public void can_build_schema_with_auto_create_none()
        {
            var id = Guid.NewGuid();

            using (var store1 = DocumentStore.For(opts =>
            {
                opts.Connection(ConnectionSource.ConnectionString);
                opts.DatabaseSchemaName = "samples";
            }))
            {
                using (var session = store1.OpenSession())
                {
                    session.Events.StartStream<Quest>(id, new QuestStarted { Name = "Destroy the Orb" },
                        new MonsterSlayed { Name = "Troll" }, new MonsterSlayed { Name = "Dragon" });
                    session.SaveChanges();
                }
            }

            #region sample_registering-event-types
            var store2 = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "samples";
                _.Connection(ConnectionSource.ConnectionString);
                _.AutoCreateSchemaObjects = AutoCreate.None;

                _.Events.AddEventType(typeof(QuestStarted));
                _.Events.AddEventType(typeof(MonsterSlayed));
            });
            #endregion

            using (var session = store2.OpenSession())
            {
                session.Events.FetchStream(id).Count.ShouldBe(3);
            }

            store2.Dispose();
        }



        public using_the_schema_objects_Tests() : base("schema_objects")
        {
        }
    }
}
