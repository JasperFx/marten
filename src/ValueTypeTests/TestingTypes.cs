using System;
using System.Runtime.CompilerServices;
using JasperFx.CodeGeneration.Frames;
using Vogen;

namespace ValueTypeTests;

[ValueObject<Guid>]
public readonly partial struct GuidId;

[ValueObject<int>]
public readonly partial struct IntId;

[ValueObject<int>]
public readonly partial struct WeirdNamed;

[ValueObject<long>]
public readonly partial struct LongId;

[ValueObject<string>]
public readonly partial struct StringId;

[ValueObject<DateOnly>]
public readonly partial struct DateId;

public record struct NewGuidId(Guid Value);
public record struct NewIntId(int Value);
public record struct NewStringId(string Value);
public record struct NewLongId(long Value);

public record struct NewDateId(DateOnly Value);

#region sample_valid_strong_typed_identifiers

// Use a constructor for the inner value,
// and expose the inner value in a *public*
// property getter
public record struct TaskId(Guid Value);

/// <summary>
/// Pair a public property getter for the inner value
/// with a public static method that takes in the
/// inner value
/// </summary>
public readonly struct Task2Id
{
    private Task2Id(Guid value) => Value = value;

    public Guid Value { get; }

    public static Task2Id From(Guid value) => new Task2Id(value);
}

#endregion




