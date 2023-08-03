using System;
using System.Collections.Generic;
using System.Linq;
using Marten;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading.Linq;

public class LinqExtensionsTests
{
    private static readonly int[] Ints = { 0, 1, 2, 3 };

    private static readonly Guid[] Guids = { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    private static readonly string[] Strings =
    {
        Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString()
    };

    [Fact]
    public void IsOneOf_shows_if_value_is_contained_in_the_specified_collection_of_ints() =>
        IsOneOf_shows_if_value_is_contained_in_the_specified_collection(Ints);

    [Fact]
    public void IsOneOf_shows_if_value_is_contained_in_the_specified_collection_of_guids() =>
        IsOneOf_shows_if_value_is_contained_in_the_specified_collection(Guids);

    [Fact]
    public void IsOneOf_shows_if_value_is_contained_in_the_specified_collection_of_strings() =>
        IsOneOf_shows_if_value_is_contained_in_the_specified_collection(Strings);

    private void IsOneOf_shows_if_value_is_contained_in_the_specified_collection<T>(T[] values)
    {
        values[1].IsOneOf(values[1], values[2]).ShouldBeTrue();
        values[1].IsOneOf(values[2], values[1]).ShouldBeTrue();
        values[1].IsOneOf(values[2], values[3]).ShouldBeFalse();
        values[1].IsOneOf(new List<T> { values[1], values[2] }).ShouldBeTrue();
        values[1].IsOneOf(new List<T> { values[2], values[1] }).ShouldBeTrue();
        values[1].IsOneOf(new List<T> { values[2], values[3] }).ShouldBeFalse();
    }

    [Fact]
    public void In_shows_if_value_is_contained_in_the_specified_collection_of_ints() =>
        In_shows_if_value_is_contained_in_the_specified_collection(Ints);

    [Fact]
    public void In_shows_if_value_is_contained_in_the_specified_collection_of_guids() =>
        In_shows_if_value_is_contained_in_the_specified_collection(Guids);

    [Fact]
    public void In_shows_if_value_is_contained_in_the_specified_collection_of_strings() =>
        In_shows_if_value_is_contained_in_the_specified_collection(Strings);

    private void In_shows_if_value_is_contained_in_the_specified_collection<T>(T[] values)
    {
        values[1].In(values[1], values[2]).ShouldBeTrue();
        values[1].In(values[2], values[1]).ShouldBeTrue();
        values[1].In(values[2], values[3]).ShouldBeFalse();
        values[1].In(new List<T> { values[1], values[2] }).ShouldBeTrue();
        values[1].In(new List<T> { values[2], values[1] }).ShouldBeTrue();
        values[1].In(new List<T> { values[2], values[3] }).ShouldBeFalse();
    }

    [Fact]
    public void IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection_of_ints() =>
        IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection(Ints);

    [Fact]
    public void IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection_of_guids() =>
        IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection(Guids);

    [Fact]
    public void IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection_of_strings() =>
        IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection(Strings);

    private void IsSupersetOf_shows_if_collection_is_superset_of_the_specified_collection<T>(T[] values)
    {
        new[] { values[1], values[2], values[3] }.IsSupersetOf(values[1], values[2]).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSupersetOf(values[2], values[3]).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSupersetOf(values[1], values[2], values[3]).ShouldBeTrue();
        new[] { values[1], values[2] }.IsSupersetOf(values[1], values[2], values[3]).ShouldBeFalse();
        new[] { values[2], values[3] }.IsSupersetOf(values[1], values[2], values[3]).ShouldBeFalse();
        new[] { values[1], values[2], values[3] }.IsSupersetOf(new List<T> { values[1], values[2] }).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSupersetOf(new List<T> { values[2], values[3] }).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSupersetOf(new List<T> { values[1], values[2], values[3] }).ShouldBeTrue();
        new[] { values[1], values[2] }.IsSupersetOf(new List<T> { values[1], values[2], values[3] }).ShouldBeFalse();
        new[] { values[2], values[3] }.IsSupersetOf(new List<T> { values[1], values[2], values[3] }).ShouldBeFalse();
    }

    [Fact]
    public void IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection_of_ints() =>
        IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection(Ints);

    [Fact]
    public void IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection_of_guids() =>
        IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection(Guids);

    [Fact]
    public void IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection_of_strings() =>
        IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection(Strings);

    private void IsSubsetOf_shows_if_collection_is_subset_of_the_specified_collection<T>(T[] values)
    {
        new[] { values[1], values[2] }.IsSubsetOf(values[1], values[2], values[3]).ShouldBeTrue();
        new[] { values[2], values[3] }.IsSubsetOf(values[1], values[2], values[3]).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSubsetOf(values[1], values[2], values[3]).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSubsetOf(values[1], values[2]).ShouldBeFalse();
        new[] { values[1], values[2], values[3] }.IsSubsetOf(values[2], values[3]).ShouldBeFalse();
        new[] { values[1], values[2] }.IsSubsetOf(new List<T> { values[1], values[2], values[3] }).ShouldBeTrue();
        new[] { values[2], values[3] }.IsSubsetOf(new List<T> { values[1], values[2], values[3] }).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSubsetOf(new List<T> { values[1], values[2], values[3] }).ShouldBeTrue();
        new[] { values[1], values[2], values[3] }.IsSubsetOf(new List<T> { values[1], values[2] }).ShouldBeFalse();
        new[] { values[1], values[2], values[3] }.IsSubsetOf(new List<T> { values[2], values[3] }).ShouldBeFalse();
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
    public void IsOneOf_throws_NotSupportedException_when_called_for_list()
    {
        Should.Throw<NotSupportedException>(() => new List<object>().IsOneOf(new List<object> {"a", "b"}));
    }

    [Fact]
    public void In_throws_NotSupportedException_when_called_for_list()
    {
        Should.Throw<NotSupportedException>(() => new List<object>().In(new List<object> {"a", "b"}));
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
