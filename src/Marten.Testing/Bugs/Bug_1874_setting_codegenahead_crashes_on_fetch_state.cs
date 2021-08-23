using Marten.Testing.Harness;
using System;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1874_setting_codegenahead_crashes_on_fetch_state: BugIntegrationContext
    {
        public class RandomEvent1
        {
            public string Status { get; set; }
        }
        [Fact]
        public void SettingCodeGenAhead_CrashesOnFetchState()
        {
            var store = StoreOptions(_ =>
            {
                _.GeneratedCodeMode = LamarCodeGeneration.TypeLoadMode.LoadFromPreBuiltAssembly;
                _.Events.AddEventType(typeof(RandomEvent1));
            });

            using var session = store.OpenSession();
            var state = session.Events.FetchStreamState(Guid.NewGuid()); // Crashing here
            state.ShouldBeNull();
        }
    }
}
