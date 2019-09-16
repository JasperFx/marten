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
        private readonly IDocumentStorage<User> theDocumentStorage = Substitute.For<IDocumentStorage<User>>();
        private readonly IQueryableDocument theMapping = DocumentMapping.For<User>().ToQueryableDocument();
        private WholeDocumentSelector<User> theSelector;

        public WholeDocumentSelectorTests()
        {
            theSelector = new WholeDocumentSelector<User>(theMapping, theDocumentStorage);
        }


        [Fact]
        public void the_selected_fields()
        {
            theSelector.SelectFields().ShouldHaveTheSameElementsAs("d.data", "d.id", "d.mt_version", "d.mt_last_modified", "d.mt_dotnet_type");
        }

        [Fact]
        public void resolves_through_identity_map()
        {
            var map = Substitute.For<IIdentityMap>();
            var reader = Substitute.For<DbDataReader>();

            var user = new User();

            theDocumentStorage.Resolve(0, reader, map).Returns(user);

            theSelector.Resolve(reader, map, null).ShouldBe(user);
        }
    }
}
