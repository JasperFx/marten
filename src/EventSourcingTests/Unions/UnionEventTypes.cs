#nullable enable
using System;

namespace EventSourcingTests.Unions;

// #5735 / jasperfx#1053: a C# 15 union appended as an event. net11.0 only (see EventSourcingTests.csproj).

public record UnionAccountOpened(string Owner);

public record UnionAccountCredited(decimal Amount);

public record UnionAccountDebited(decimal Amount, string Reason);

public union UnionAccountEvent(UnionAccountCredited, UnionAccountDebited);

/// <summary>Self-aggregating, with one Apply for the whole union (jasperfx#1053).</summary>
public partial class UnionAccount
{
    public Guid Id { get; set; }
    public string Owner { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public int Debits { get; set; }

    public static UnionAccount Create(UnionAccountOpened e) => new() { Owner = e.Owner };

    public void Apply(UnionAccountEvent e)
    {
        switch (e)
        {
            case UnionAccountCredited credited:
                Balance += credited.Amount;
                break;
            case UnionAccountDebited debited:
                Balance -= debited.Amount;
                Debits++;
                break;
        }
    }
}
