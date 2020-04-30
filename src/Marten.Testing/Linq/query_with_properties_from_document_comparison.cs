using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_with_properties_from_document_comparison : IntegrationContextWithIdentityMap<NulloIdentityMap>
    {
        [Fact]
        public void compares_properties_correctly()
        {
            var target1 = new Target
            {
                Id = Guid.NewGuid(),
                Number = 1,
                AnotherNumber = 2
            };

            var target2 = new Target
            {
                Id = Guid.NewGuid(),
                Number = 20,
                AnotherNumber = 10
            };

            theSession.Store(target1, target2);
            theSession.SaveChanges();

            var result = theSession.Query<Target>()
                .Where(t => t.AnotherNumber > t.Number)
                .ToList();

            result.ShouldHaveSingleItem();
            SpecificationExtensions.ShouldContain(result, t => t.Id == target1.Id);
        }

        public query_with_properties_from_document_comparison(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
