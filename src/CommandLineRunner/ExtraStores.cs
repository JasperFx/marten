using System;
using Marten;

namespace CommandLineRunner;

public interface IThingStore: IDocumentStore;

public class Thing
{
    public Guid Id { get; set; }
}

public class Thing2
{
    public Guid Id { get; set; }
}

public class Thing3
{
    public Guid Id { get; set; }
}
