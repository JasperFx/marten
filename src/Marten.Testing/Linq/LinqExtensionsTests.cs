using System;
using System.Collections.Generic;
using System.Linq;
using Remotion.Linq.Parsing.ExpressionVisitors.MemberBindings;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class LinqExtensionsTests
    {
        [Fact]
        public void IsOneOf_shows_if_value_is_contained_in_the_specified_collection()
        {
            1.IsOneOf(1, 2).ShouldBeTrue();
            1.IsOneOf(2, 1).ShouldBeTrue();
            1.IsOneOf(2, 3).ShouldBeFalse();
            1.IsOneOf(new List<int> {1, 2}).ShouldBeTrue();
            1.IsOneOf(new List<int> {2, 1}).ShouldBeTrue();
            1.IsOneOf(new List<int> {2, 3}).ShouldBeFalse();
        }

        [Fact]
        public void In_shows_if_value_is_contained_in_the_specified_collection()
        {
            1.In(1, 2).ShouldBeTrue();
            1.In(2, 1).ShouldBeTrue();
            1.In(2, 3).ShouldBeFalse();
            1.In(new List<int> {1, 2}).ShouldBeTrue();
            1.In(new List<int> {2, 1}).ShouldBeTrue();
            1.In(new List<int> {2, 3}).ShouldBeFalse();
        }

        [Fact]
        public void IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection()
        {
            new[] {1, 2, 3}.IsSupersetOf(1, 2).ShouldBeTrue();
            new[] {1, 2, 3}.IsSupersetOf(2, 3).ShouldBeTrue();
            new[] {1, 2, 3}.IsSupersetOf(1, 2, 3).ShouldBeTrue();
            new[] {1, 2}.IsSupersetOf(1, 2, 3).ShouldBeFalse();
            new[] {2, 3}.IsSupersetOf(1, 2, 3).ShouldBeFalse();
            new[] {1, 2, 3}.IsSupersetOf(new List<int> {1, 2}).ShouldBeTrue();
            new[] {1, 2, 3}.IsSupersetOf(new List<int> {2, 3}).ShouldBeTrue();
            new[] {1, 2, 3}.IsSupersetOf(new List<int> {1, 2, 3}).ShouldBeTrue();
            new[] {1, 2}.IsSupersetOf(new List<int> {1, 2, 3}).ShouldBeFalse();
            new[] {2, 3}.IsSupersetOf(new List<int> {1, 2, 3}).ShouldBeFalse();
        }

        [Fact]
        public void IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection()
        {
            new[] {1, 2}.IsSubsetOf(1, 2, 3).ShouldBeTrue();
            new[] {2, 3}.IsSubsetOf(1, 2, 3).ShouldBeTrue();
            new[] {1, 2, 3}.IsSubsetOf(1, 2, 3).ShouldBeTrue();
            new[] {1, 2, 3}.IsSubsetOf(1, 2).ShouldBeFalse();
            new[] {1, 2, 3}.IsSubsetOf(2, 3).ShouldBeFalse();
            new[] {1, 2}.IsSubsetOf(new List<int> {1, 2, 3}).ShouldBeTrue();
            new[] {2, 3}.IsSubsetOf(new List<int> {1, 2, 3}).ShouldBeTrue();
            new[] {1, 2, 3}.IsSubsetOf(new List<int> {1, 2, 3}).ShouldBeTrue();
            new[] {1, 2, 3}.IsSubsetOf(new List<int> {1, 2}).ShouldBeFalse();
            new[] {1, 2, 3}.IsSubsetOf(new List<int> {2, 3}).ShouldBeFalse();
        }

        [Fact]
        public void IsEmpty_shows_if_collection_is_null_or_empty()
        {
            ((IEnumerable<object>)null).IsEmpty().ShouldBeTrue();
            Enumerable.Empty<object>().IsEmpty().ShouldBeTrue();
            Enumerable.Repeat(new object(), 1).IsEmpty().ShouldBeFalse();
            ((string)(null)).IsEmpty().ShouldBeTrue();
            "".IsEmpty().ShouldBeTrue();
            " ".IsEmpty().ShouldBeFalse();
            "a".IsEmpty().ShouldBeFalse();
        }

        [Fact]
        public void TenantIsOneOf_throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(() => new object().TenantIsOneOf("a", "b"));
        }

        [Fact]
        public void Search_throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(() => new object().Search("search term"));
            Should.Throw<NotSupportedException>(() => new object().Search("search term", "reg conf"));
        }

        [Fact]
        public void PlainTextSearch_throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(() => new object().PlainTextSearch("search term"));
            Should.Throw<NotSupportedException>(() => new object().PlainTextSearch("search term", "reg conf"));
        }

        [Fact]
        public void PhraseSearch_throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(() => new object().PhraseSearch("search term"));
            Should.Throw<NotSupportedException>(() => new object().PhraseSearch("search term", "reg conf"));
        }

        [Fact]
        public void WebStyleSearch_throws_NotSupportedException_when_called_directly()
        {
            Should.Throw<NotSupportedException>(() => new object().WebStyleSearch("search term"));
            Should.Throw<NotSupportedException>(() => new object().WebStyleSearch("search term", "reg conf"));
        }
    }
}
