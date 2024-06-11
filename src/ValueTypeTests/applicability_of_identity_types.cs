using System;
using Marten;
using Marten.Schema;
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


    }

    [Theory]
    [InlineData(typeof(GuidId), typeof(GuidId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(IntId), typeof(IntId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(LongId), typeof(LongId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(StringId), typeof(StringId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(GuidId?), typeof(GuidId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(IntId?), typeof(IntId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(LongId?), typeof(LongId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(StringId?), typeof(StringId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(NewGuidId), typeof(NewGuidId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(NewIntId), typeof(NewIntId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(NewLongId), typeof(NewLongId), typeof(StrongTypedIdGeneration))]
    [InlineData(typeof(NewStringId), typeof(NewStringId), typeof(StrongTypedIdGeneration))]
    public void find_and_apply_id_type(Type idType, Type expectedIdType, Type expectedGenerationType)
    {
        var documentType = typeof(Document<>).MakeGenericType(idType);
        var mapping = new DocumentMapping(documentType, new StoreOptions());
        mapping.IdType.ShouldBe(expectedIdType);
        mapping.IdStrategy.ShouldBeOfType(expectedGenerationType);
    }
}

public class Document<T>
{
    public T Id { get; set; }
}
