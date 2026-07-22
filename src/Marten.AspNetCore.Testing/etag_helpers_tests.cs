using System;
using Marten.AspNetCore;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Marten.AspNetCore.Testing;

/// <summary>
/// Unit coverage for the <see cref="ETagHelpers"/> logic branches that the Alba
/// endpoint tests don't exercise directly: the <c>*</c> wildcard, <c>W/</c> weak
/// validator stripping, and multi-value comma-separated <c>If-None-Match</c> lists.
/// </summary>
public class etag_helpers_tests
{
    private static HttpContext contextWithIfNoneMatch(params string[] values)
    {
        var context = new DefaultHttpContext();
        if (values.Length > 0)
        {
            context.Request.Headers["If-None-Match"] = values;
        }

        return context;
    }

    [Fact]
    public void format_guid_version_is_quoted_lowercase_d()
    {
        var version = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");
        ETagHelpers.Format(version).ShouldBe("\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\"");
    }

    [Fact]
    public void format_long_version_is_quoted()
    {
        ETagHelpers.Format(17L).ShouldBe("\"17\"");
    }

    [Fact]
    public void no_if_none_match_header_never_matches()
    {
        var context = contextWithIfNoneMatch();
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeFalse();
    }

    [Fact]
    public void exact_match_returns_true()
    {
        var context = contextWithIfNoneMatch("\"1\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeTrue();
    }

    [Fact]
    public void non_matching_value_returns_false()
    {
        var context = contextWithIfNoneMatch("\"2\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeFalse();
    }

    [Fact]
    public void wildcard_matches_any_etag()
    {
        var context = contextWithIfNoneMatch("*");
        ETagHelpers.IfNoneMatchMatches(context, "\"anything\"").ShouldBeTrue();
    }

    [Fact]
    public void weak_validator_prefix_is_stripped_before_comparison()
    {
        var context = contextWithIfNoneMatch("W/\"1\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeTrue();
    }

    [Fact]
    public void weak_validator_prefix_is_case_insensitive()
    {
        var context = contextWithIfNoneMatch("w/\"1\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeTrue();
    }

    [Fact]
    public void multi_value_comma_separated_list_matches_any_member()
    {
        var context = contextWithIfNoneMatch("\"7\", \"8\", \"1\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeTrue();
    }

    [Fact]
    public void multi_value_list_with_weak_members_matches()
    {
        var context = contextWithIfNoneMatch("W/\"7\", W/\"1\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeTrue();
    }

    [Fact]
    public void multi_value_list_with_no_match_returns_false()
    {
        var context = contextWithIfNoneMatch("\"7\", \"8\", \"9\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeFalse();
    }

    [Fact]
    public void multiple_header_values_are_all_considered()
    {
        // A client may send several If-None-Match header lines; any hit wins.
        var context = contextWithIfNoneMatch("\"7\"", "\"1\"");
        ETagHelpers.IfNoneMatchMatches(context, "\"1\"").ShouldBeTrue();
    }

    [Fact]
    public void empty_etag_never_matches()
    {
        var context = contextWithIfNoneMatch("*");
        ETagHelpers.IfNoneMatchMatches(context, "").ShouldBeFalse();
    }
}
