#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LinqTests.Unions;

// #5735: the C# 15 unions the union LINQ tests query. net11.0 only (see LinqTests.csproj).
//
// System.Text.Json writes a union as its bare active case (jasperfx#1037). Reading one back takes a
// classifier as soon as two cases are JSON objects, and the structural classifier refuses a union where
// one case's property names contain another's even on WRITE. So UnionPayment, whose object cases are
// disjoint, carries the structural classifier and round-trips; UnionLedgerEntry, the typical event-shaped
// union, can be stored but never read back as a union, and its tests only count or project ids.

public record UnionCard(string Last4);

public record UnionCash(decimal Amount);

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union UnionPayment(UnionCard, UnionCash);

/// <summary>Primitive cases, told apart by JSON kind; STJ reads these back with no classifier.</summary>
public union UnionCode(int, string);

public record UnionDeposited(decimal Amount);

public record UnionWithdrawn(decimal Amount, string Reason);

/// <summary>One case's shape contains the other's: Deposited is recognised by Reason being absent.</summary>
public union UnionLedgerEntry(UnionDeposited, UnionWithdrawn);

public record UnionMeters(double Value);

public record UnionFeet(double Value);

/// <summary>Two cases with identical shapes: no stored value says which one it is.</summary>
public union UnionLength(UnionMeters, UnionFeet);

/// <summary>Two cases written as a JSON number.</summary>
public union UnionNumber(int, double);

public record UnionTagged(string? Note);

public record UnionPlain();

/// <summary>Tagged's only property is nullable, and a null is left out, so a Tagged can look like a Plain.</summary>
public union UnionMaybeTagged(UnionTagged, UnionPlain);

/// <summary>A case that is itself a union.</summary>
public union UnionNested(UnionPayment, string);

public class UnionHolder
{
    public UnionPayment Payment { get; set; }
}

public class UnionOrder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public UnionPayment Payment { get; set; }
    public UnionCode Code { get; set; }
    public UnionPayment? Maybe { get; set; }
    public UnionHolder? Holder { get; set; }
}

public class UnionLedgerDoc
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public UnionLedgerEntry Entry { get; set; }
}

public class UnionRefusalDoc
{
    public Guid Id { get; set; }
    public UnionLength Length { get; set; }
    public UnionNumber Number { get; set; }
    public UnionMaybeTagged Tag { get; set; }
    public UnionNested Nested { get; set; }
    public object? Anything { get; set; }
}

public class UnionLine
{
    public string Sku { get; set; } = string.Empty;
    public UnionPayment Payment { get; set; }
}

public class UnionBasket
{
    public Guid Id { get; set; }
    public List<UnionLine> Lines { get; set; } = new();
}
