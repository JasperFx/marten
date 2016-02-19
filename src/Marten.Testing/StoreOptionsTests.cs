using System;
using System.Linq;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Xunit;

namespace Marten.Testing
{
    public class StoreOptionsTests
    {
        [Fact]
        public void add_document_types()
        {
            var options = new StoreOptions();
            options.RegisterDocumentType<User>();
            options.RegisterDocumentType(typeof(Company));
            options.RegisterDocumentTypes(new Type[] {typeof(Target), typeof(Issue)});

            options.AllDocumentMappings.OrderBy(x => x.DocumentType.Name).Select(x => x.DocumentType.Name)
                .ShouldHaveTheSameElementsAs("Company", "Issue", "Target", "User");
        }
    }
}