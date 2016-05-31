namespace Marten.Schema
{
    public interface IIndexDefinition
    {
        string IndexName { get; }
        string ToDDL();
        bool Matches(ActualIndex index);
    }
}