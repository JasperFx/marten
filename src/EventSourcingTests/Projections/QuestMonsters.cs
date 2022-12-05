using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using Newtonsoft.Json;

namespace EventSourcingTests.Projections;

public class Root
{
    public Guid Id { get; set; }
}

public interface IMonstersView
{
    Guid Id { get; }
    string[] Monsters { get; }
}

public class QuestMonsters
{
    public Guid Id { get; set; }

    private readonly IList<string> _monsters = new List<string>();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithPrivateIdSetter: IMonstersView
{
    public Guid Id { get; private set; }

    private readonly IList<string> _monsters = new List<string>();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithProtectedIdSetter: IMonstersView
{
    public Guid Id { get; protected set; }

    private readonly IList<string> _monsters = new List<string>();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithPrivateConstructor: IMonstersView
{
    public Guid Id { get; protected set; }

    private readonly IList<string> _monsters = new List<string>();

    private QuestMonstersWithPrivateConstructor()
    {

    }

    public static QuestMonstersWithPrivateConstructor Init() => new QuestMonstersWithPrivateConstructor();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithBaseClass: Root, IMonstersView
{
    private readonly IList<string> _monsters = new List<string>();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithBaseClassAndIdOverloaded: Root, IMonstersView
{
    public new Guid Id { get; set; }

    private readonly IList<string> _monsters = new List<string>();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithBaseClassAndIdOverloadedWithNew: Root, IMonstersView
{
    public new Guid Id { get; set; }

    private readonly IList<string> _monsters = new List<string>();

    public void Apply(MonsterSlayed slayed)
    {
        _monsters.Fill(slayed.Name);
    }

    public string[] Monsters
    {
        get { return _monsters.ToArray(); }
        set
        {
            _monsters.Clear();
            _monsters.AddRange(value);
        }
    }
}

public class QuestMonstersWithNonDefaultPublicConstructor: IMonstersView
{
    public Guid Id { get; private set; }

    public IList<string> Monsters { get; private set; }

    string[] IMonstersView.Monsters => Monsters.ToArray();

    public QuestMonstersWithNonDefaultPublicConstructor(
        Guid id,
        string[] monsters
    )
    {
        Id = id;
        Monsters = monsters;
    }

    private QuestMonstersWithNonDefaultPublicConstructor()
    {

    }

    public void Apply(MonsterSlayed slayed)
    {
        Monsters ??= new List<string>();

        Monsters.Fill(slayed.Name);
    }
}

public class WithDefaultPrivateConstructorNonDefaultPublicConstructor: IMonstersView
{
    public Guid Id { get; private set; }

    public IList<string> Monsters { get; private set; } = new List<string>();

    string[] IMonstersView.Monsters => Monsters.ToArray();

    public WithDefaultPrivateConstructorNonDefaultPublicConstructor(
        Guid id,
        string[] monsters
    )
    {
        Id = id;
        Monsters = monsters;
    }

    public WithDefaultPrivateConstructorNonDefaultPublicConstructor()
    {
    }

    public void Apply(MonsterSlayed slayed)
    {
        Monsters.Fill(slayed.Name);
    }
}

public class WithMultiplePublicNonDefaultConstructors: IMonstersView
{
    public Guid Id { get; private set; }

    public IList<string> Monsters { get; private set; } = new List<string>();

    string[] IMonstersView.Monsters => Monsters.ToArray();

    public WithMultiplePublicNonDefaultConstructors(
        Guid id,
        string[] monsters
    ) : this(id)
    {
        Monsters = monsters;
    }

    public WithMultiplePublicNonDefaultConstructors(
        Guid id
    )
    {
        Id = id;
    }

    private WithMultiplePublicNonDefaultConstructors()
    {

    }

    public void Apply(MonsterSlayed slayed)
    {
        Monsters.Fill(slayed.Name);
    }
}

public class WithMultiplePrivateNonDefaultConstructors: IMonstersView
{
    public Guid Id { get; private set; }

    public IList<string> Monsters { get; private set; } = new List<string>();

    string[] IMonstersView.Monsters => Monsters.ToArray();

    private WithMultiplePrivateNonDefaultConstructors(
        Guid id,
        string[] monsters
    ) : this(id)
    {
        Monsters = monsters;
    }

    private WithMultiplePrivateNonDefaultConstructors(
        Guid id
    )
    {
        Id = id;
    }

    public WithMultiplePrivateNonDefaultConstructors()
    {

    }

    public void Apply(MonsterSlayed slayed)
    {
        Monsters.Fill(slayed.Name);
    }
}

public class WithMultiplePrivateNonDefaultConstructorsAndAttribute: IMonstersView
{
    public Guid Id { get; private set; }

    public IList<string> Monsters { get; private set; } = new List<string>();

    string[] IMonstersView.Monsters => Monsters.ToArray();

    [JsonConstructor]
    private WithMultiplePrivateNonDefaultConstructorsAndAttribute(
        Guid id,
        string[] monsters
    )
    {
        Id = id;
        Monsters = monsters;
    }

    private WithMultiplePrivateNonDefaultConstructorsAndAttribute(
        Guid id,
        string dummyParameters,
        string toHaveThemMore,
        string thanConstructorWithAttribute
    )
    {
        Id = id;
    }

    private WithMultiplePrivateNonDefaultConstructorsAndAttribute()
    {

    }

    public void Apply(MonsterSlayed slayed)
    {
        Monsters.Fill(slayed.Name);
    }
}

public class WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount: IMonstersView
{
    public Guid Id { get; private set; }

    public IList<string> Monsters { get; private set; } = new List<string>();

    string[] IMonstersView.Monsters => Monsters.ToArray();


    private WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount(
        Guid id,
        string dummyParametersToEqualPublicConstructorParamsCount
    )
    {
        Id = id;
    }

    public WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount(
        Guid id,
        string[] monsters
    )
    {
        Monsters = monsters;
    }

    private WithNonDefaultConstructorsPrivateAndPublicWithEqualParamsCount()
    {

    }

    public void Apply(MonsterSlayed slayed)
    {
        Monsters.Fill(slayed.Name);
    }
}
