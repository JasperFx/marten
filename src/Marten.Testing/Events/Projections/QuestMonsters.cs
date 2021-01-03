using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;

namespace Marten.Testing.Events.Projections
{
    public class Root
    {
        public Guid Id { get; set; }
    }

    public interface IMonstersView
    {
        Guid Id { get; }
        List<string> Monsters { get; }
    }

    public class QuestMonsters
    {
        public Guid Id { get; set; }

        public List<string> Monsters { get; set; } = new();

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithPrivateIdSetter: IMonstersView
    {
        public Guid Id { get; private set; }

        public List<string> Monsters { get; set; } = new();

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithProtectedIdSetter: IMonstersView
    {
        public Guid Id { get; protected set; }

        public List<string> Monsters { get; set; } = new();

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithBaseClass: Root, IMonstersView
    {
        public List<string> Monsters { get; set; } = new();

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithBaseClassAndIdOverloaded: Root, IMonstersView
    {
        public new Guid Id { get; set; }

        public List<string> Monsters { get; set; } = new();

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithBaseClassAndIdOverloadedWithNew: Root, IMonstersView
    {
        public new Guid Id { get; set; }

        public List<string> Monsters { get; set; } = new();

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithNonDefaultPublicConstructor: IMonstersView
    {
        public Guid Id { get; private set; }

        public List<string> Monsters { get; set; } = new();

        public QuestMonstersWithNonDefaultPublicConstructor(
            Guid id,
            List<string> monsters
        )
        {
            Id = id;
            Monsters = monsters;
        }

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithDefaultPrivateConstructorAndNonDefaultPublicConstructor: IMonstersView
    {
        public Guid Id { get; private set; }

        public List<string> Monsters { get; set; } = new();

        public QuestMonstersWithDefaultPrivateConstructorAndNonDefaultPublicConstructor(
            Guid id,
            List<string> monsters
        )
        {
            Id = id;
            Monsters = monsters;
        }

        private QuestMonstersWithDefaultPrivateConstructorAndNonDefaultPublicConstructor()
        {
        }

        public void Apply(MonsterSlayed slayed)
        {
            Monsters.Fill(slayed.Name);
        }
    }
}
