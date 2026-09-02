using Marten.Util;
using Shouldly;
using Xunit;

namespace CoreTests.Util;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("data", "d.data")]
    [InlineData("(data ->> 'Title')", "(d.data ->> 'Title')")]
    [InlineData("((data ->> 'Title') || ' ' || (data ->> 'LastName'))",
        "((d.data ->> 'Title') || ' ' || (d.data ->> 'LastName'))")]
    [InlineData("data #>> '{Inner,Name}'", "d.data #>> '{Inner,Name}'")]
    public void qualifies_the_data_column(string sql, string expected)
    {
        sql.ApplyTableAliasToDataColumn("d").ShouldBe(expected);
    }

    // https://github.com/JasperFx/marten/issues/5314 — a naive Replace("data", "d.data") rewrote the
    // JSON key as readily as the column, so a member whose serialized name contains "data" searched
    // a key that does not exist.
    [Theory]
    [InlineData("(data ->> 'data')", "(d.data ->> 'data')")]
    [InlineData("(data ->> 'metadata')", "(d.data ->> 'metadata')")]
    [InlineData("(data ->> 'data_v2')", "(d.data ->> 'data_v2')")]
    [InlineData("(coalesce(data ->> 'data', '') || ' ' || coalesce(data ->> 'title', ''))",
        "(coalesce(d.data ->> 'data', '') || ' ' || coalesce(d.data ->> 'title', ''))")]
    // a doubled quote escapes a quote inside the literal, so the literal does not end early
    [InlineData("(data ->> 'it''s data')", "(d.data ->> 'it''s data')")]
    public void leaves_json_keys_alone(string sql, string expected)
    {
        sql.ApplyTableAliasToDataColumn("d").ShouldBe(expected);
    }

    [Theory]
    [InlineData("(metadata ->> 'Title')")]
    [InlineData("(data_v2 ->> 'Title')")]
    public void leaves_other_identifiers_alone(string sql)
    {
        sql.ApplyTableAliasToDataColumn("d").ShouldBe(sql);
    }

    [Fact]
    public void is_idempotent()
    {
        const string sql = "((data ->> 'data') || ' ' || (data ->> 'title'))";

        var once = sql.ApplyTableAliasToDataColumn("d");
        once.ApplyTableAliasToDataColumn("d").ShouldBe(once);
    }

    [Fact]
    public void round_trips_with_RemoveTableAlias()
    {
        const string aliased = "((d.data ->> 'data') || ' ' || (d.data ->> 'metadata'))";

        aliased.RemoveTableAlias("d").ApplyTableAliasToDataColumn("d").ShouldBe(aliased);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void handles_empty_input(string sql)
    {
        sql.ApplyTableAliasToDataColumn("d").ShouldBe(sql);
    }
}
