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
    [InlineData(typeof(FSharpTypes.OrderId), false)]
    public void ValueTypeIdGeneration_IsCandidate(Type candidate, bool isCandidate)
    {
        var value = ValueTypeIdGeneration.IsCandidate(candidate, out var idGeneration);
        value.ShouldBe(isCandidate);
    }

    [Theory]
    [InlineData(typeof(FSharpTypes.OrderId), true)]
    [InlineData(typeof(FSharpTypes.OrderIdDUWithMultipleCases), false)]
    [InlineData(typeof(FSharpTypes.RecordTypeOrderId), false)]
    [InlineData(typeof(FSharpTypes.ArbitraryClass), false)]
    public void FSharpIdGeneration_IsCandidate(Type candidate, bool isCandidate)
    {
        var value = FSharpDiscriminatedUnionIdGeneration.IsCandidate(candidate, out var idGeneration);
        value.ShouldBe(isCandidate);
    }

    [Theory]
    [InlineData(typeof(GuidId), typeof(GuidId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(IntId), typeof(IntId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(LongId), typeof(LongId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(StringId), typeof(StringId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(GuidId?), typeof(GuidId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(IntId?), typeof(IntId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(LongId?), typeof(LongId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(StringId?), typeof(StringId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(NewGuidId), typeof(NewGuidId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(NewIntId), typeof(NewIntId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(NewLongId), typeof(NewLongId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(NewStringId), typeof(NewStringId), typeof(ValueTypeIdGeneration))]
    [InlineData(typeof(FSharpTypes.OrderId), typeof(FSharpTypes.OrderId), typeof(FSharpDiscriminatedUnionIdGeneration))]
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
