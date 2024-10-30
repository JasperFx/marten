using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Operators;

// TODO -- move this all to where_clauses
public class is_one_of_operator: IntegrationContext
{
    public static TheoryData<Func<int[], Expression<Func<Target, bool>>>> SupportedIsOneOfWithIntArray =
        new()
        {
            validNumbers => x => x.Number.IsOneOf(validNumbers),
            validNumbers => x => x.Number.In(validNumbers)
        };

    public static TheoryData<Func<int[], Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithIntArray =
        new()
        {
            validNumbers => x => !x.Number.IsOneOf(validNumbers),
            validNumbers => x => !x.Number.In(validNumbers)
        };

    public static TheoryData<Func<List<int>, Expression<Func<Target, bool>>>> SupportedIsOneOfWithIntList =
        new()
        {
            validNumbers => x => x.Number.IsOneOf(validNumbers),
            validNumbers => x => x.Number.In(validNumbers)
        };

    public static TheoryData<Func<List<int>, Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithIntList =
        new()
        {
            validNumbers => x => !x.Number.IsOneOf(validNumbers),
            validNumbers => x => !x.Number.In(validNumbers)
        };

    public static TheoryData<Func<Guid[], Expression<Func<Target, bool>>>> SupportedIsOneOfWithGuidArray =
        new()
        {
            validGuids => x => x.OtherGuid.IsOneOf(validGuids),
            validGuids => x => x.OtherGuid.In(validGuids)
        };

    public static TheoryData<Func<Guid[], Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithGuidArray =
        new()
        {
            validGuids => x => !x.OtherGuid.IsOneOf(validGuids),
            validGuids => x => !x.OtherGuid.In(validGuids)
        };

    public static TheoryData<Func<List<Guid>, Expression<Func<Target, bool>>>> SupportedIsOneOfWithGuidList =
        new()
        {
            validGuids => x => x.OtherGuid.IsOneOf(validGuids),
            validGuids => x => x.OtherGuid.In(validGuids)
        };

    public static TheoryData<Func<List<Guid>, Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithGuidList =
        new()
        {
            validGuids => x => !x.OtherGuid.IsOneOf(validGuids),
            validGuids => x => !x.OtherGuid.In(validGuids)
        };

    public static TheoryData<Func<string[], Expression<Func<Target, bool>>>> SupportedIsOneOfWithStringArray =
        new()
        {
            validStrings => x => x.String.IsOneOf(validStrings),
            validStrings => x => x.String.In(validStrings)
        };

    public static TheoryData<Func<string[], Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithStringArray =
        new()
        {
            validStrings => x => !x.String.IsOneOf(validStrings),
            validStrings => x => !x.String.In(validStrings)
        };

    public static TheoryData<Func<List<string>, Expression<Func<Target, bool>>>> SupportedIsOneOfWithStringList =
        new()
        {
            validStrings => x => x.String.IsOneOf(validStrings),
            validStrings => x => x.String.In(validStrings)
        };

    public static TheoryData<Func<List<string>, Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithStringList =
        new()
        {
            validStrings => x => !x.String.IsOneOf(validStrings),
            validStrings => x => !x.String.In(validStrings)
        };

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithIntArray))]
    public Task can_query_against_integers(Func<int[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithGuidArray))]
    public Task can_query_against_guids(Func<Guid[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.OtherGuid);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithStringArray))]
    public Task can_query_against_strings(Func<string[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.String);

    private async Task can_query_against_array<T>(Func<T[], Expression<Func<Target, bool>>> isOneOf, Func<Target, T> select)
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        var validValues = targets.Select(select).Distinct().Take(3).ToArray();

        var found = theSession.Query<Target>().Where(isOneOf(validValues)).ToArray();

        found.Length.ShouldBeLessThan(100);

        var expected = targets
            .Where(x => validValues.Contains(select(x)))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArray();

        found.OrderBy(x => x.Id).Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected);
    }

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithIntArray))]
    public Task can_query_against_integers_with_not_operator(Func<int[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithGuidArray))]
    public Task can_query_against_guids_with_not_operator(Func<Guid[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.OtherGuid);


    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithStringArray))]
    public Task can_query_against_strings_with_not_operator(Func<string[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.String);

    private async Task can_query_against_array_with_not_operator<T>(
        Func<T[], Expression<Func<Target, bool>>> notIsOneOf,
        Func<Target, T> select
    )
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        var validValues = targets.Select(select).Distinct().Take(3).ToArray();

        var found = theSession.Query<Target>().Where(notIsOneOf(validValues)).ToArray();

        var expected = targets
            .Where(x => !validValues.Contains(select(x)))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArray();

        found.OrderBy(x => x.Id).Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected);
    }

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithIntList))]
    public Task can_query_against_integers_list(Func<List<int>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithGuidList))]
    public Task can_query_against_guids_list(Func<List<Guid>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.OtherGuid);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithStringList))]
    public Task can_query_against_strings_list(Func<List<string>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.String);

    private async Task can_query_against_list<T>(Func<List<T>, Expression<Func<Target, bool>>> isOneOf, Func<Target, T> select)
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        var validValues = targets.Select(select).Distinct().Take(3).ToList();

        var found = theSession.Query<Target>().Where(isOneOf(validValues)).ToArray();

        found.Length.ShouldBeLessThan(100);

        var expected = targets
            .Where(x => validValues.Contains(select(x)))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToArray();

        found.OrderBy(x => x.Id).Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected);
    }

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithIntList))]
    public Task can_query_against_integers_with_not_operator_list(
        Func<List<int>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithGuidList))]
    public Task can_query_against_guids_with_not_operator_list(
        Func<List<Guid>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.OtherGuid);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithStringList))]
    public Task can_query_against_strings_with_not_operator_list(
        Func<List<string>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.String);

    private async Task can_query_against_list_with_not_operator<T>(
        Func<List<T>, Expression<Func<Target, bool>>> notIsOneOf,
        Func<Target, T> select
    )
    {
        var targets = Target.GenerateRandomData(100).ToArray();
        await theStore.BulkInsertAsync(targets);

        var validValues = targets.Select(select).Distinct().Take(3).ToList();

        var found = theSession.Query<Target>().Where(notIsOneOf(validValues)).ToArray();

        var expected = targets
            .Where(x => !validValues.Contains(select(x)))
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToList();

        found.OrderBy(x => x.Id).Select(x => x.Id)
            .ShouldHaveTheSameElementsAs(expected);
    }



    public is_one_of_operator(DefaultStoreFixture fixture): base(fixture)
    {
    }
}
