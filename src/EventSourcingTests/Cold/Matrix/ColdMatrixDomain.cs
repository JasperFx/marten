#pragma warning disable MARTEN001 // cold event storage is experimental
using System.Collections.Generic;
using JasperFx;
using JasperFx.Events.Aggregation;

namespace EventSourcingTests.Cold.Matrix;

// The domain of the cold storage matrix. One stream shape (opened, then three items) folded by one aggregate type
// per combination of lifecycle, write cache and identity, so a single store can hold every combination that
// shares its store-wide settings.

public record MatrixOrderNumber(string Value);

public record MatrixOpened(string Customer, MatrixOrderNumber Number);

public record MatrixItemAdded(string Name);

public abstract class MatrixCart: IRevisioned
{
    public string Id { get; set; } = null!;
    public int Version { get; set; }
    public string? Customer { get; set; }
    public List<string> Items { get; set; } = new();

    public void Apply(MatrixItemAdded e) => Items.Add(e.Name);
}

/// <summary>Fetched by its stream key.</summary>
public abstract class KeyedMatrixCart: MatrixCart
{
    public void Apply(MatrixOpened e) => Customer = e.Customer;
}

/// <summary>Fetched by its natural key, the order number.</summary>
public abstract class NumberedMatrixCart: MatrixCart
{
    [NaturalKey]
    public MatrixOrderNumber Number { get; set; } = null!;

    [NaturalKeySource]
    public void Apply(MatrixOpened e)
    {
        Number = e.Number;
        Customer = e.Customer;
    }
}

public class LiveKeyedCart: KeyedMatrixCart;
public class LiveKeyedCachedCart: KeyedMatrixCart;
public class LiveNumberedCart: NumberedMatrixCart;
public class LiveNumberedCachedCart: NumberedMatrixCart;

public class InlineKeyedCart: KeyedMatrixCart;
public class InlineKeyedCachedCart: KeyedMatrixCart;
public class InlineNumberedCart: NumberedMatrixCart;
public class InlineNumberedCachedCart: NumberedMatrixCart;

public class AsyncKeyedCart: KeyedMatrixCart;
public class AsyncKeyedCachedCart: KeyedMatrixCart;
public class AsyncNumberedCart: NumberedMatrixCart;
public class AsyncNumberedCachedCart: NumberedMatrixCart;

/// <summary>How the matrix addresses a stream.</summary>
public enum ColdIdentity
{
    /// <summary>
    ///     Refused with a cold store at startup today (string stream identity only). Kept as a column of the matrix
    ///     so Guid streams can be added without reshaping it; see <c>guid_stream_identity_with_a_cold_store</c>.
    /// </summary>
    Guid,

    /// <summary>The stream key.</summary>
    String,

    /// <summary>A natural key on a string-keyed stream.</summary>
    NaturalKey
}

public enum ColdLifecycle
{
    Live,
    Inline,
    Async
}

/// <summary>Where the stream ends up before the fetch.</summary>
public enum ColdShape
{
    /// <summary>Four events, cold through version 2.</summary>
    PartlyCold,

    /// <summary>Four events, all of them cold.</summary>
    FullyCold,

    /// <summary>
    ///     Three events, cold through 2, then a fourth appended after the move and the stream moved again through 3:
    ///     cold, append, cold again.
    /// </summary>
    ColdTwice
}

/// <summary>What became of an Inline or Async aggregate's stored document when its stream went cold.</summary>
public enum ColdDocument
{
    /// <summary>Live aggregates have none.</summary>
    None,

    /// <summary>Left in place.</summary>
    Kept,

    /// <summary>Moved out with the stream's events, as an application that moves read-model rows does.</summary>
    MovedOut
}

public enum ColdFetch
{
    FetchForWriting,
    FetchForWritingWithExpectedVersion,
    FetchForExclusiveWriting,
    BatchedFetchForWriting,
    BatchedFetchForWritingWithExpectedVersion,
    FetchManyForWriting,
    FetchLatest,
    BatchedFetchLatest,
    ProjectLatest,
    AggregateStreamAsync
}

public enum ColdSession
{
    Lightweight,
    IdentityMap,
    DirtyTracked
}
