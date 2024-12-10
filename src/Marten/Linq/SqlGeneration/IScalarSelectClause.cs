#nullable enable
namespace Marten.Linq.SqlGeneration;

public interface IScalarSelectClause
{
    string MemberName { get; }
    void ApplyOperator(string op);
    ISelectClause CloneToDouble();

    ISelectClause CloneToOtherTable(string tableName);
}
