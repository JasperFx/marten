using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Linq.CursorPaging;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading;

public class cursor_paging_tests: IntegrationContext
{
    public cursor_paging_tests(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    protected override async Task fixtureSetup()
    {
        await theStore.Advanced.ResetAllData();
    }

    private async Task seedUsers(params string[] firstNames)
    {
        foreach (var name in firstNames)
        {
            theSession.Store(new User { FirstName = name, LastName = "Smith" });
        }

        await theSession.SaveChangesAsync();
    }

    [Fact]
    public async Task first_page_with_no_cursor_returns_items_and_next_cursor()
    {
        await seedUsers("a", "b", "c", "d", "e");

        var page = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 2);

        page.Count.ShouldBe(2);
        page.NextCursor.ShouldNotBeNullOrEmpty();
        page.ItemsJson.ShouldContain("\"a\"");
        page.ItemsJson.ShouldContain("\"b\"");
    }

    [Fact]
    public async Task subsequent_page_with_cursor_returns_next_items_with_no_overlap()
    {
        await seedUsers("a", "b", "c", "d", "e");

        var first = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 2);

        var second = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(first.NextCursor, pageSize: 2);

        second.Count.ShouldBe(2);
        second.ItemsJson.ShouldContain("\"c\"");
        second.ItemsJson.ShouldContain("\"d\"");
        second.ItemsJson.ShouldNotContain("\"a\"");
        second.ItemsJson.ShouldNotContain("\"b\"");
        second.NextCursor.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task last_page_has_fewer_rows_than_page_size_and_no_next_cursor()
    {
        await seedUsers("a", "b", "c", "d", "e");

        var first = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 2);

        var second = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(first.NextCursor, pageSize: 2);

        var third = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(second.NextCursor, pageSize: 2);

        third.Count.ShouldBe(1);
        third.ItemsJson.ShouldContain("\"e\"");
        third.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task exact_multiple_of_page_size_ends_with_no_next_cursor()
    {
        await seedUsers("a", "b", "c", "d");

        var first = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 2);

        first.NextCursor.ShouldNotBeNullOrEmpty();

        var second = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(first.NextCursor, pageSize: 2);

        second.Count.ShouldBe(2);
        second.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task empty_result_set_yields_empty_page_and_no_cursor()
    {
        await theStore.Advanced.ResetAllData();

        var page = await theSession.Query<User>()
            .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 5);

        page.Count.ShouldBe(0);
        page.ItemsJson.ShouldBe("[]");
        page.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task mixed_sort_directions_paginate_correctly()
    {
        await seedUsers("a", "b", "c", "d", "e");

        var first = await theSession.Query<User>()
            .OrderByDescending(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(cursor: null, pageSize: 2);

        first.ItemsJson.ShouldContain("\"e\"");
        first.ItemsJson.ShouldContain("\"d\"");
        first.NextCursor.ShouldNotBeNullOrEmpty();

        var second = await theSession.Query<User>()
            .OrderByDescending(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(first.NextCursor, pageSize: 2);

        second.ItemsJson.ShouldContain("\"c\"");
        second.ItemsJson.ShouldContain("\"b\"");

        var third = await theSession.Query<User>()
            .OrderByDescending(x => x.FirstName).ThenBy(x => x.Id)
            .ToJsonPageByCursorAsync(second.NextCursor, pageSize: 2);

        third.ItemsJson.ShouldContain("\"a\"");
        third.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task duplicate_leading_sort_key_is_disambiguated_by_terminal_tie_breaker()
    {
        await seedUsers("same", "same", "same", "same", "same");

        var seenCount = 0;
        string? cursor = null;

        for (var i = 0; i < 10; i++)
        {
            var page = await theSession.Query<User>()
                .OrderBy(x => x.FirstName).ThenBy(x => x.Id)
                .ToJsonPageByCursorAsync(cursor, pageSize: 2);

            seenCount += page.Count;
            cursor = page.NextCursor;

            if (cursor == null) break;
        }

        seenCount.ShouldBe(5);
    }

    [Fact]
    public async Task missing_order_by_throws()
    {
        await seedUsers("a");

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await theSession.Query<User>().ToJsonPageByCursorAsync(cursor: null, pageSize: 2));
    }

    [Fact]
    public async Task non_unique_terminal_sort_key_throws()
    {
        await seedUsers("a", "b");

        // FirstName alone is not guaranteed unique - must end with a unique member (Id)
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await theSession.Query<User>().OrderBy(x => x.FirstName)
                .ToJsonPageByCursorAsync(cursor: null, pageSize: 2));
    }

    [Fact]
    public async Task non_positive_page_size_throws()
    {
        await seedUsers("a");

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await theSession.Query<User>().OrderBy(x => x.FirstName).ThenBy(x => x.Id)
                .ToJsonPageByCursorAsync(cursor: null, pageSize: 0));
    }
}
