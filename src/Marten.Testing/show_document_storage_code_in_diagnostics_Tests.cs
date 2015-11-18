using System.Diagnostics;
using Marten.Schema;
using Marten.Testing.Fixtures;
using StructureMap;

namespace Marten.Testing
{
    public class show_document_storage_code_in_diagnostics_Tests
    {
        public void show_code()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            container.GetInstance<IDocumentSchema>().Alter(_ =>
            {
                _.For<Target>().Searchable(x => x.Date);
            });

            var code = container.GetInstance<IDocumentSession>().Diagnostics.DocumentStorageCodeFor<Target>();
            Debug.WriteLine(code);
        } 

    }
}