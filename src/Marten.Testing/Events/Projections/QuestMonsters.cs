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

        public void Apply(MonsterSlayed slayed)
        {
            if (Monsters == null)
                Monsters = new List<string>();

            Monsters.Fill(slayed.Name);
        }
    }

    public class QuestMonstersWithDefaultPrivateConstructorAndNonDefaultPublicConstructor: IMonstersView
    {
        public Guid Id { get; private set; }

        public IList<string> Monsters { get; private set; } = new List<string>();

        string[] IMonstersView.Monsters => Monsters.ToArray();

        public QuestMonstersWithDefaultPrivateConstructorAndNonDefaultPublicConstructor(
            Guid id,
            string[] monsters
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
