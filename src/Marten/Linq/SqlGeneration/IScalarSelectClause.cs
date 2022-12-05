namespace Marten.Linq.SqlGeneration;

internal interface IScalarSelectClause
{
    string FieldName { get; }
    void ApplyOperator(string op);
    ISelectClause CloneToDouble();

    ISelectClause CloneToOtherTable(string tableName);
}
