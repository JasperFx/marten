using System;
using System.Data.Common;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Services.Includes;
using Marten.Testing.Documents;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Services.Includes
{


    public class IncludeSelectorTests
    {
        private IDocumentMapping theMapping = DocumentMappingFactory.For<User>();
        private Action<User> theCallback = Substitute.For<Action<User>>();
        private ISelector<Issue> inner = Substitute.For<ISelector<Issue>>();
        private IResolver<User> theResolver = Substitute.For<IResolver<User>>();
        private IncludeSelector<Issue, User> theSelector;

        public IncludeSelectorTests()
        {
            inner.SelectFields().Returns(new string[] {"a", "b", "c"});

            theSelector = new IncludeSelector<Issue, User>("foo", theMapping, theCallback, inner, theResolver);
        }

        [Fact]
        public void the_starting_index_is_one_after_the_inner_fields()
        {
            theSelector.StartingIndex.ShouldBe(3);
        }

        [Fact]
        public void select_fields_has_both_the_inner_and_outer_fields()
        {
            theSelector.SelectFields().ShouldHaveTheSameElementsAs("a", "b", "c", "foo.data", "foo.id");
        }

        [Fact]
        public void can_resolve_and_callback_from_the_reader()
        {
            var reader = Substitute.For<DbDataReader>();
            var issue = new Issue();
            var user = new User();

            var map = new NulloIdentityMap(null);

            theResolver.Resolve(3, reader, map).Returns(user);
            inner.Resolve(reader, map).Returns(issue);

            theSelector.Resolve(reader, map).ShouldBe(issue);

            theCallback.Received().Invoke(user);
        }
    }
}