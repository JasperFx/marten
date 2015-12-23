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
            var container = Container.For<DevelopmentModeRegistry>();
            container.GetInstance<IDocumentSchema>().Alter(_ =>
            {
                _.For<Target>().Searchable(x => x.Date);
            });

            var code = container.GetInstance<IDocumentStore>().Diagnostics.DocumentStorageCodeFor<Target>();
            Debug.WriteLine(code);
        } 

    }
}