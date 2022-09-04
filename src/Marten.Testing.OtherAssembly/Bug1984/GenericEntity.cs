using System;

namespace Marten.Testing.OtherAssembly.Bug1984;

public class GenericEntity<T>
{
    public Guid Id { get; set; }
    public T Data { get; set; }
}