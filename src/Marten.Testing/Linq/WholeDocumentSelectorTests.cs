using System;
using System.Data.Common;
using System.Diagnostics;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class WholeDocumentSelectorTests
    {
        private readonly IResolver<User> theResolver = Substitute.For<IResolver<User>>();
        private readonly IQueryableDocument theMapping = DocumentMapping.For<User>().ToQueryableDocument();
        private WholeDocumentSelector<User> theSelector;

        public WholeDocumentSelectorTests()
        {
            theSelector = new WholeDocumentSelector<User>(theMapping, theResolver);
        }


        [Fact]
        public void the_selected_fields()
        {
            theSelector.SelectFields().ShouldHaveTheSameElementsAs("d.data", "d.id");
        }

        [Fact]
        public void resolves_through_identity_map()
        {
            var map = Substitute.For<IIdentityMap>();
            var reader = Substitute.For<DbDataReader>();

            var user = new User();

            theResolver.Resolve(0, reader, map).Returns(user);

            theSelector.Resolve(reader, map).ShouldBe(user);
        }
    }
}