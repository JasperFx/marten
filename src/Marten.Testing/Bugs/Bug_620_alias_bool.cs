using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_620_alias_bool : BugIntegrationContext
    {
        [Fact]
        public async Task can_canonicize_bool()
        {
            using (var store1 = SeparateStore())
            {
                await store1.EnsureStorageExistsAsync(typeof(DocWithBool));

                await store1.Schema.ApplyAllConfiguredChangesToDatabaseAsync();
            }

            using (var store2 = SeparateStore(_ =>
            {
                _.Schema.For<DocWithBool>();
            }))
            {
                await store2.Schema.AssertDatabaseMatchesConfigurationAsync();
            }
        }
    }

    public class DocWithBool
    {
        public Guid Id;

        [DuplicateField(PgType = "bool")]
        public bool IsTrue { get; set; }
    }
}
