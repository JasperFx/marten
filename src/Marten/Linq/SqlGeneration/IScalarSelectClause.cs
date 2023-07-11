namespace Marten.Linq.SqlGeneration;

internal interface IScalarSelectClause
{
    string MemberName { get; }
    void ApplyOperator(string op);
    ISelectClause CloneToDouble();

    ISelectClause CloneToOtherTable(string tableName);
}
