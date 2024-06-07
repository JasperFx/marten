using System;
using Marten.Schema.Identity;
using Shouldly;

namespace ValueTypeTests;

public class applicability_of_identity_types
{
    [Theory]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(GuidId), true)]
    [InlineData(typeof(IntId), true)]
    [InlineData(typeof(LongId), true)]
    [InlineData(typeof(StringId), true)]
    [InlineData(typeof(GuidId?), true)]
    [InlineData(typeof(IntId?), true)]
    [InlineData(typeof(LongId?), true)]
    [InlineData(typeof(StringId?), true)]
    [InlineData(typeof(DateId), false)]
    [InlineData(typeof(DateId?), false)]
    [InlineData(typeof(NewGuidId), true)]
    [InlineData(typeof(NewIntId), true)]
    [InlineData(typeof(NewLongId), true)]
    [InlineData(typeof(NewStringId), true)]
    [InlineData(typeof(NewDateId), false)]
    public void StrongTypedIdGeneration_IsCandidate(Type candidate, bool isCandidate)
    {
        var value = StrongTypedIdGeneration.IsCandidate(candidate, out var idGeneration);
        value.ShouldBe(isCandidate);
        if (value)
        {
            idGeneration.ShouldBeOfType<StrongTypedIdGeneration>();
        }

    }
}
