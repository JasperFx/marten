using System;
using System.Threading.Tasks;
using LinqTests.Acceptance.Support;
using Microsoft.FSharp.Core;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class where_clauses_fsharp: LinqTestContext<where_clauses_fsharp>
{
    public where_clauses_fsharp(DefaultQueryFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestOutput = output;
    }

    static where_clauses_fsharp()
    {

        @where(x => x.FSharpBoolOption == FSharpOption<bool>.Some(true));
        @where(x => x.FSharpBoolOption == FSharpOption<bool>.Some(false));
        @where(x => x.FSharpDateOption == FSharpOption<DateTime>.Some(DateTime.Now));
        @where(x => x.FSharpIntOption == FSharpOption<int>.Some(300));
        @where(x => x.FSharpStringOption == FSharpOption<string>.Some("My String"));
        @where(x => x.FSharpLongOption == FSharpOption<long>.Some(5_000_000));

        //Comparing options is not a valid syntax in C#, we therefore define these expressions in F#
        @where(FSharpTypes.greaterThanWithFsharpDateOption);
        @where(FSharpTypes.lesserThanWithFsharpDateOption);
        @where(FSharpTypes.greaterThanWithFsharpStringOption);
        @where(FSharpTypes.lesserThanWithFsharpStringOption);
        @where(FSharpTypes.greaterThanWithFsharpDecimalOption);
        @where(FSharpTypes.lesserThanWithFsharpDecimalOption);
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task run_query(string description)
    {
        return assertFSharpTestCase(description, Fixture.FSharpFriendlyStore);
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task with_duplicated_fields(string description)
    {
        return assertFSharpTestCase(description, Fixture.FSharpFriendlyStoreWithDuplicatedField);
    }
}
