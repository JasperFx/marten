#nullable enable
using System;
using JasperFx.Events.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace Marten.Events.Projections;

/// <summary>
/// Marks a type as being able to register itself into an IoC container for Marten
/// </summary>
public interface IMartenRegistrable
{
    static abstract void Register<TConcrete>(IServiceCollection collection, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure) where TConcrete : class;

    static abstract void Register<TConcrete, TStore>(IServiceCollection collection, ProjectionLifecycle lifecycle,
        ServiceLifetime lifetime, Action<ProjectionBase>? configure) where TStore : IDocumentStore where TConcrete : class;
}
