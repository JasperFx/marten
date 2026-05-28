namespace Marten.PgVector;

/// <summary>
/// Distance functions supported by pgvector for similarity search.
/// </summary>
public enum DistanceFunction
{
    /// <summary>
    /// Euclidean (L2) distance. Operator: &lt;-&gt;
    /// Index ops class: vector_l2_ops
    /// </summary>
    L2,

    /// <summary>
    /// Cosine distance. Operator: &lt;=&gt;
    /// Index ops class: vector_cosine_ops
    /// Most common for text embeddings.
    /// </summary>
    Cosine,

    /// <summary>
    /// Inner product (negative). Operator: &lt;#&gt;
    /// Index ops class: vector_ip_ops
    /// </summary>
    InnerProduct
}

internal static class DistanceFunctionExtensions
{
    public static string Operator(this DistanceFunction f) => f switch
    {
        DistanceFunction.L2 => "<->",
        DistanceFunction.Cosine => "<=>",
        DistanceFunction.InnerProduct => "<#>",
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };

    public static string OpsClass(this DistanceFunction f) => f switch
    {
        DistanceFunction.L2 => "vector_l2_ops",
        DistanceFunction.Cosine => "vector_cosine_ops",
        DistanceFunction.InnerProduct => "vector_ip_ops",
        _ => throw new ArgumentOutOfRangeException(nameof(f))
    };
}
