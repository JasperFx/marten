using System;
using JasperFx.CodeGeneration.Frames;
using Vogen;

namespace ValueTypeTests;

[ValueObject<Guid>]
public partial struct GuidId;

[ValueObject<int>]
public partial struct IntId;

[ValueObject<long>]
public partial struct LongId;

[ValueObject<string>]
public partial struct StringId;

[ValueObject<DateOnly>]
public partial struct DateId;

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
public struct Task2Id
{
    private Task2Id(Guid value) => Value = value;

    public Guid Value { get; }

    public static Task2Id From(Guid value) => new Task2Id(value);
}

#endregion

#region F# discriminated union

public abstract class OrderId
{
    // Nested class for the 'Id' case
    public sealed class Id : OrderId
    {
        public Guid Value { get; } // Property to hold the Guid

        public Id(Guid value) // Constructor
        {
            Value = value;
        }

        // Overridden equality and GetHashCode for proper comparison
        public override bool Equals(object obj) => obj is Id other && Value.Equals(other.Value);
        public override int GetHashCode() => Value.GetHashCode();
    }

    // Private constructor to prevent direct instantiation of OrderId
    private OrderId() { }

    // Static factory methods for creating instances
    public static OrderId NewId(Guid value) => new Id(value);
}

#endregion



