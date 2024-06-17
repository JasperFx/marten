using System;
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

public struct SpecialValue
{
    private SpecialValue(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static SpecialValue From(string value) => new SpecialValue(value);
}

public class NotValidId(string Value);

public class DefinitelyNotValid;
