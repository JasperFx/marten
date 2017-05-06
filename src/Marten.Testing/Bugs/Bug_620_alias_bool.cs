using System;
using Marten.Schema;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_620_alias_bool 
    {
        [Fact]
        public void can_canonicize_bool()
        {
            using (var store1 = TestingDocumentStore.Basic())
            {
                store1.Tenancy.Default.EnsureStorageExists(typeof(DocWithBool));

                store1.Schema.ApplyAllConfiguredChangesToDatabase();
            }

            using (var store2 = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<DocWithBool>();
            }))
            {
                store2.Schema.AssertDatabaseMatchesConfiguration();
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