using Newtonsoft.Json;

namespace DocumentDbTests.HierarchicalStorage
{
    public class WindowsPolicy: OsPolicy
    {
        [JsonIgnore] public override PolicyType Type { get; protected set; } = PolicyType.Windows;
    }
}