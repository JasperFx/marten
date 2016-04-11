using System.Diagnostics;
using Marten.Schema;
using Marten.Testing.Fixtures;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class show_document_storage_code_in_diagnostics_Tests
    {
        [Fact]
        public void show_code()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                _.Schema.For<Target>().Searchable(x => x.Date);

            }))
            {
                var code = store.Diagnostics.DocumentStorageCodeFor<Target>();
                Debug.WriteLine(code);
            }

        } 

    }
}