using System;
using System.Collections.Generic;
using JasperFx.Events.Projections;

namespace JasperFx.Events.ComplianceTests;

/// <summary>
/// Store-neutral description of the event store configuration a compliance suite needs.
/// A suite fills one of these in; the fixture replays it against its store through
/// <see cref="IComplianceStoreRegistrar"/>.
/// </summary>
/// <remarks>
/// The public lists exist so a fixture can inspect what was asked for (for example, to decide
/// whether the schema needs event tag tables). The actual registration runs through the recorded
/// generic closures in <see cref="ApplyTo"/>, in declaration order.
/// </remarks>
public sealed class ComplianceStoreConfig
{
    private readonly List<Action<IComplianceStoreRegistrar>> _registrations = new();

    /// <summary>
    /// Optional schema/namespace override. When null the fixture picks its own.
    /// </summary>
    public string? SchemaName { get; set; }

    public List<Type> EventTypes { get; } = new();

    public List<(Type Tag, string Suffix, Type? Aggregate)> TagTypes { get; } = new();

    public List<(Type Doc, SnapshotLifecycle Lifecycle)> Snapshots { get; } = new();

    public List<Type> LiveAggregations { get; } = new();

    public ComplianceStoreConfig AddEventType<T>()
    {
        EventTypes.Add(typeof(T));
        _registrations.Add(registrar => registrar.AddEventType(typeof(T)));
        return this;
    }

    public ComplianceStoreConfig RegisterTagType<TTag>(string tableSuffix, Type? aggregateType = null)
        where TTag : notnull
    {
        TagTypes.Add((typeof(TTag), tableSuffix, aggregateType));
        _registrations.Add(registrar =>
        {
            var registration = registrar.RegisterTagType<TTag>(tableSuffix);
            if (aggregateType != null)
            {
                registration.ForAggregate(aggregateType);
            }
        });

        return this;
    }

    public ComplianceStoreConfig Snapshot<TDoc>(SnapshotLifecycle lifecycle) where TDoc : notnull
    {
        Snapshots.Add((typeof(TDoc), lifecycle));
        _registrations.Add(registrar => registrar.Snapshot<TDoc>(lifecycle));
        return this;
    }

    public ComplianceStoreConfig LiveAggregation<TDoc>() where TDoc : notnull
    {
        LiveAggregations.Add(typeof(TDoc));
        _registrations.Add(registrar => registrar.LiveAggregation<TDoc>());
        return this;
    }

    public void ApplyTo(IComplianceStoreRegistrar registrar)
    {
        foreach (var registration in _registrations)
        {
            registration(registrar);
        }
    }
}
