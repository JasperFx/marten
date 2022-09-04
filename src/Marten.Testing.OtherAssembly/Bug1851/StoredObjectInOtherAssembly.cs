using System;

namespace Marten.Testing.OtherAssembly.Bug1851;

public class StoredObjectInOtherAssembly
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}