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
}
