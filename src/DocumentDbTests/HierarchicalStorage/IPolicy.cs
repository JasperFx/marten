namespace DocumentDbTests.HierarchicalStorage
{
    public interface IPolicy: IVersioned
    {
        PolicyType Type { get; }
        string Name { get; set; }
    }
}