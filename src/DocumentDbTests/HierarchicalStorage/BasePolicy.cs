using System;
using Newtonsoft.Json;

namespace DocumentDbTests.HierarchicalStorage;

public abstract class BasePolicy: IPolicy
{
    public Guid VersionId { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; } = Guid.NewGuid();

    [JsonIgnore] public abstract PolicyType Type { get; protected set; }

    public string Name { get; set; }
}