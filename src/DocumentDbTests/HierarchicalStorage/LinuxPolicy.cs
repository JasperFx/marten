using Newtonsoft.Json;

namespace DocumentDbTests.HierarchicalStorage
{
    public class LinuxPolicy: OsPolicy
    {
        [JsonIgnore] public override PolicyType Type { get; protected set; } = PolicyType.Linux;
    }
}