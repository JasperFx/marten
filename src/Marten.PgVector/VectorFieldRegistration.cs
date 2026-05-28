using System.Linq.Expressions;
using System.Reflection;

namespace Marten.PgVector;

/// <summary>
/// Captures a vector field registration before it's applied to the DocumentMapping.
/// </summary>
internal class VectorFieldRegistration
{
    public Type DocumentType { get; }
    public MemberInfo Member { get; }
    public int Dimensions { get; }
    public DistanceFunction Distance { get; }
    public string ColumnName { get; }

    public VectorFieldRegistration(Type documentType, MemberInfo member, int dimensions,
        DistanceFunction distance, string? columnName)
    {
        DocumentType = documentType;
        Member = member;
        Dimensions = dimensions;
        Distance = distance;
        ColumnName = columnName ?? member.Name.ToLowerInvariant();
    }
}

/// <summary>
/// Configuration builder for pgvector options.
/// </summary>
public class PgVectorOptions
{
    internal List<VectorFieldRegistration> Registrations { get; } = new();

    /// <summary>
    /// Register a vector column on a document type. The property must be of type
    /// Pgvector.Vector or float[].
    /// </summary>
    public PgVectorOptions VectorOn<TDoc>(
        Expression<Func<TDoc, object?>> memberExpression,
        int dimensions,
        DistanceFunction distance = DistanceFunction.Cosine,
        string? columnName = null)
    {
        var member = GetMemberInfo(memberExpression);
        Registrations.Add(new VectorFieldRegistration(
            typeof(TDoc), member, dimensions, distance, columnName));
        return this;
    }

    private static MemberInfo GetMemberInfo<TDoc>(Expression<Func<TDoc, object?>> expression)
    {
        var body = expression.Body;

        // Handle convert/unbox for value types
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        return body switch
        {
            MemberExpression memberExpr => memberExpr.Member,
            _ => throw new ArgumentException("Expression must be a simple property or field access")
        };
    }
}
