using System;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Exceptions;
using Shouldly;

namespace ValueTypeTests;

public class registration
{
    [Fact]
    public void register_happy_path()
    {
        var options = new StoreOptions();
        var value = options.RegisterValueType(typeof(ExternalId));
        value.Ctor.ShouldNotBeNull();
        value.ValueProperty.Name.ShouldBe("Value");
    }

    [Fact]
    public void register_happy_path_with_static_method()
    {
        var options = new StoreOptions();
        var value = options.RegisterValueType(typeof(SpecialValue));
        value.Builder.Name.ShouldBe("From");
        value.ValueProperty.Name.ShouldBe("Value");
    }

    [Fact]
    public void picks_builder_whose_return_type_matches_value_type()
    {
        var options = new StoreOptions();
        var value = options.RegisterValueType(typeof(SpecialValueWithNullableSibling));    
        value.Builder.Name.ShouldBe("From");
        value.Builder.ReturnType.ShouldBe(typeof(SpecialValueWithNullableSibling));
    }

    [Theory]
    [InlineData(typeof(NotValidId))]
    [InlineData(typeof(DefinitelyNotValid))]
    public void sad_path_registration(Type type)
    {
        var options = new StoreOptions();
        Should.Throw<InvalidValueTypeException>(() =>
        {
            options.RegisterValueType(type);
        });
    }
}

public record struct ExternalId(string Value);

public readonly struct SpecialValue
{
    private SpecialValue(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SpecialValue From(string value) => new SpecialValue(value);
}

public readonly struct SpecialValueWithNullableSibling
{
    private SpecialValueWithNullableSibling(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SpecialValueWithNullableSibling? FromNullable(string? value)
        => value is null ? null : From(value);

    public static SpecialValueWithNullableSibling From(string value) => new(value);
}

public class NotValidId(string Value);

public class DefinitelyNotValid;
