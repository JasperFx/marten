using Baseline;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Xunit;

namespace Marten.Testing.Schema
{
    public class WritePatch_smoke_tests : IntegratedFixture
    {
        [Fact]
        public void writes_both_the_update_and_rollback_files()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<User>();
                _.Events.AddEventType(typeof(MembersJoined));
            });

            var fileSystem = new FileSystem();
            fileSystem.DeleteDirectory("patches");
            fileSystem.CreateDirectory("patches");

            theStore.Schema.WritePatch("patches".AppendPath("1.initial.sql"));

            fileSystem.FileExists("patches".AppendPath("1.initial.sql"));
            fileSystem.FileExists("patches".AppendPath("1.initial.drop.sql"));
        }
    }
}