using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;

namespace Marten.Testing.Schema
{
    public class DocumentStorageBuilderTests
    {
        public void do_not_blow_up_building_one()
        {
            var storage = DocumentStorageBuilder.Build(null, new DocumentMapping(typeof(User)));

            storage.ShouldNotBeNull();
        }

        public void do_not_blow_up_building_more_than_one()
        {
            var mappings = new DocumentMapping[]
            {
                new DocumentMapping(typeof(User)), 
                new DocumentMapping(typeof(Company)), 
                new DocumentMapping(typeof(Issue)), 
            };

            DocumentStorageBuilder.Build(null, mappings).Count()
                .ShouldBe(3);
        }

    }
}