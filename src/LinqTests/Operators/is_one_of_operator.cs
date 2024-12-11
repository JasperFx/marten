using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Serialization;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Microsoft.FSharp.Core;
using Shouldly;

namespace LinqTests.Operators;

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

    public static TheoryData<Func<FSharpOption<Guid>[], Expression<Func<Target, bool>>>> SupportedIsOneOfWithFsharpGuidOptionArray =
        new()
        {
            validGuids => x => x.FSharpGuidOption.IsOneOf(validGuids),
            validGuids => x => x.FSharpGuidOption.In(validGuids)
        };

    public static TheoryData<Func<FSharpOption<Guid>[], Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithFsharpGuidOptionArray =
        new()
        {
            validGuids => x => !x.FSharpGuidOption.IsOneOf(validGuids),
            validGuids => x => !x.FSharpGuidOption.In(validGuids)
        };

    public static TheoryData<Func<List<FSharpOption<Guid>>, Expression<Func<Target, bool>>>> SupportedIsOneOfWithFsharpGuidOptionList =
        new()
        {
            validGuids => x => x.FSharpGuidOption.IsOneOf(validGuids),
            validGuids => x => x.FSharpGuidOption.In(validGuids)
        };

    public static TheoryData<Func<List<FSharpOption<Guid>>, Expression<Func<Target, bool>>>> SupportedNotIsOneOfWithFsharpGuidOptionList =
        new()
        {
            validGuids => x => !x.FSharpGuidOption.IsOneOf(validGuids),
            validGuids => x => !x.FSharpGuidOption.In(validGuids)
        };



    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithIntArray))]
    public void can_query_against_integers(Func<int[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithGuidArray))]
    public void can_query_against_guids(Func<Guid[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.OtherGuid);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithStringArray))]
    public void can_query_against_strings(Func<string[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.String);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithFsharpGuidOptionArray))]
    public void can_query_against_fsharp_guid_option_array(Func<FSharpOption<Guid>[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.FSharpGuidOption);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithFsharpGuidOptionArray))]
    public void can_query_against_fsharp_guid_option_array_with_unwrapped_guid(Func<FSharpOption<Guid>[], Expression<Func<Target, bool>>> isOneOf) =>
        can_query_against_array(isOneOf, x => x.FSharpGuidOption);

    private void can_query_against_array<T>(Func<T[], Expression<Func<Target, bool>>> isOneOf, Func<Target, T> select)
    {

        var targets = Target.GenerateRandomData(100, true).ToArray();
        theStore.BulkInsert(targets);

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
    public void can_query_against_integers_with_not_operator(Func<int[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithGuidArray))]
    public void can_query_against_guids_with_not_operator(Func<Guid[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.OtherGuid);


    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithStringArray))]
    public void can_query_against_strings_with_not_operator(Func<string[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.String);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithFsharpGuidOptionArray))]
    public void can_query_against_fsharp_guid_option_with_not_operator(Func<FSharpOption<Guid>[], Expression<Func<Target, bool>>> notIsOneOf)
        => can_query_against_array_with_not_operator(notIsOneOf, x => x.FSharpGuidOption);

    private void can_query_against_array_with_not_operator<T>(
        Func<T[], Expression<Func<Target, bool>>> notIsOneOf,
        Func<Target, T> select
    )
    {
        var targets = Target.GenerateRandomData(100, true).ToArray();
        theStore.BulkInsert(targets);

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
    public void can_query_against_integers_list(Func<List<int>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithGuidList))]
    public void can_query_against_guids_list(Func<List<Guid>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.OtherGuid);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithStringList))]
    public void can_query_against_strings_list(Func<List<string>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.String);

    [Theory]
    [MemberData(nameof(SupportedIsOneOfWithFsharpGuidOptionList))]
    public void can_query_against_fsharp_guid_option_list(Func<List<FSharpOption<Guid>>, Expression<Func<Target, bool>>> isOneOf)
        => can_query_against_list(isOneOf, x => x.FSharpGuidOption);

    private void can_query_against_list<T>(Func<List<T>, Expression<Func<Target, bool>>> isOneOf, Func<Target, T> select)
    {
        var targets = Target.GenerateRandomData(100, true).ToArray();
        theStore.BulkInsert(targets);

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
    public void can_query_against_integers_with_not_operator_list(
        Func<List<int>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.Number);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithGuidList))]
    public void can_query_against_guids_with_not_operator_list(
        Func<List<Guid>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.OtherGuid);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithStringList))]
    public void can_query_against_strings_with_not_operator_list(
        Func<List<string>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.String);

    [Theory]
    [MemberData(nameof(SupportedNotIsOneOfWithFsharpGuidOptionList))]
    public void can_query_against_fsharp_guid_option_with_not_operator_list(
        Func<List<FSharpOption<Guid>>, Expression<Func<Target, bool>>> notIsOneOf) =>
        can_query_against_list_with_not_operator(notIsOneOf, x => x.FSharpGuidOption);

    private void can_query_against_list_with_not_operator<T>(
        Func<List<T>, Expression<Func<Target, bool>>> notIsOneOf,
        Func<Target, T> select
    )
    {
        var targets = Target.GenerateRandomData(100, true).ToArray();
        theStore.BulkInsert(targets);

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
        StoreOptions(_ =>
        {
            //_.Logger(new ConsoleMartenLogger());
            _.RegisterValueType(typeof(FSharpOption<Guid>));
            _.DisableNpgsqlLogging = false;
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            _.UseSystemTextJsonForSerialization(serializerOptions);
        });
    }
}
