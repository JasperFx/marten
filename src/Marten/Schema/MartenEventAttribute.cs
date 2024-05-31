#nullable enable
using System;
using JasperFx.Core;

namespace Marten.Schema;

/// <summary>
/// Just marks a type as being a persisted Marten event for the AutoRegister()
/// feature
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class MartenEventAttribute: MartenAttribute
{
    /// <summary>
    /// Use to override the Marten event type alias
    /// </summary>
    public string? Alias { get; set; }

    public override void Register(Type discoveredType, StoreOptions options)
    {
        options.Events.AddEventType(discoveredType);
        if (Alias.IsNotEmpty())
        {

            options.Events.MapEventType(discoveredType, Alias);
        }
    }
}
