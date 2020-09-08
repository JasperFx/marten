namespace Marten.Linq.SqlGeneration
{
    public interface IScalarSelectClause
    {
        void ApplyOperator(string op);
        ISelectClause CloneToDouble();
        string FieldName { get; }

        ISelectClause CloneToOtherTable(string tableName);
    }
}
