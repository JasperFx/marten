using Marten.Schema;

namespace Marten.Storage
{
    public interface IIndexDefinition
    {
        string IndexName { get; }

        string ToDDL();

        bool Matches(ActualIndex index);
    }
}
