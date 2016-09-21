using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;

namespace Marten.Testing.Events.Projections
{
    // SAMPLE: WeaponNamesByQuest
// Aggregates

public class Quest
{
    public Guid Id { get; set; }
    public string Name { get; set; }

    public void Apply(QuestStarted questStarted)
    {
        Id = questStarted.Id;
        Name = questStarted.Name;
    }
}

public class Person
{
    public Guid Id { get; set; }
    public Guid QuestId { get; set; }

    public void Apply(PersonCreated personCreated)
    {
        Id = personCreated.Id;
        QuestId = personCreated.QuestId;
    }
}

public class Weapon
{
    public Guid Id { get; set; }
    public Guid HolderId { get; set; }
    public string Name { get; set; }

    public void Apply(WeaponCreated weaponCreated)
    {
        Id = weaponCreated.Id;
        HolderId = weaponCreated.HolderId;
        Name = weaponCreated.Name;
    }

    public void Apply(WeaponNameChanged nameChanged)
    {
        Name = nameChanged.Name;
    }
}

// Events

public class QuestStarted
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class PersonCreated
{
    public Guid Id { get; set; }
    public Guid QuestId { get; set; }
}

public class WeaponCreated
{
    public Guid Id { get; set; }
    public Guid HolderId { get; set; }
    public string Name { get; set; }
}

public class WeaponNameChanged
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

// Projections

public class WeaponNamesByQuest
{
    public Guid Id { get; set; }

    // Store the weapon names keyed by Id so we can change names if needed
    public Dictionary<Guid, string> WeaponNamesLookup { get; set; }
        = new Dictionary<Guid, string>();

    public IEnumerable<string> WeaponNames => WeaponNamesLookup.Values;
}

public class WeaponNamesByQuestProjection : ViewProjection<WeaponNamesByQuest>
{
    public WeaponNamesByQuestProjection()
    {
        ProjectEvent<WeaponCreated>(
            (session, evt) => session.Load<Person>(evt.HolderId).QuestId,
            (view, evt) => view.WeaponNamesLookup[evt.Id] = evt.Name);
        ProjectEvent<WeaponNameChanged>(
            (session, evt) => session.Load<Person>(session.Load<Weapon>(evt.Id).HolderId).QuestId,
            (view, evt) => view.WeaponNamesLookup[evt.Id] = evt.Name);
    }
}
    // ENDSAMPLE
}
