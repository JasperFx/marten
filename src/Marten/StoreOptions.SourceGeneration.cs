using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marten.Events.Projections;

namespace Marten;

public partial class StoreOptions
{
    /// <summary>
    ///     Attempt to use a source-generated type manifest from the given assembly
    ///     (or the ApplicationAssembly if not specified) to register document types,
    ///     projection types, and event types at startup without runtime assembly scanning.
    ///     This is an opt-in feature that requires the Marten.SourceGeneration analyzer
    ///     package to be referenced in the consuming project.
    /// </summary>
    /// <param name="assembly">
    ///     The assembly to search for the generated manifest. Defaults to ApplicationAssembly.
    /// </param>
    /// <returns>True if a source-generated manifest was found and applied; false otherwise.</returns>
    public bool TryUseSourceGeneratedDiscovery(Assembly? assembly = null)
    {
        assembly ??= ApplicationAssembly;
        if (assembly == null) return false;

        var manifestType = assembly.GetType("Marten.Generated.DiscoveredMartenTypes");
        if (manifestType == null) return false;

        ApplySourceGeneratedManifest(manifestType);
        return true;
    }

    /// <summary>
    ///     Use a source-generated type manifest to register document types,
    ///     projection types, and event types. This is a convenience method
    ///     that directly accepts the manifest type from the generated code.
    /// </summary>
    /// <param name="manifestType">The generated DiscoveredMartenTypes type.</param>
    public void UseSourceGeneratedDiscovery(Type manifestType)
    {
        ApplySourceGeneratedManifest(manifestType);
    }

    private void ApplySourceGeneratedManifest(Type manifestType)
    {
        // Register document types
        var documentTypesProp = manifestType.GetProperty("DocumentTypes",
            BindingFlags.Public | BindingFlags.Static);
        if (documentTypesProp != null)
        {
            var documentTypes = documentTypesProp.GetValue(null) as IReadOnlyList<Type>;
            if (documentTypes != null)
            {
                foreach (var docType in documentTypes)
                {
                    RegisterDocumentType(docType);
                }
            }
        }

        // Register event types
        var eventTypesProp = manifestType.GetProperty("EventTypes",
            BindingFlags.Public | BindingFlags.Static);
        if (eventTypesProp != null)
        {
            var eventTypes = eventTypesProp.GetValue(null) as IReadOnlyList<Type>;
            if (eventTypes != null)
            {
                foreach (var eventType in eventTypes)
                {
                    Events.AddEventType(eventType);
                }
            }
        }

        // Note: Projection registration is NOT done automatically here because
        // projections require a ProjectionLifecycle (Inline, Async, Live) which
        // cannot be reliably inferred at compile time. Users should continue to
        // register projections explicitly via StoreOptions.Projections.Add<T>().
        // The manifest's ProjectionTypes property is available for tooling and
        // diagnostics (e.g., drift detection).
    }
}
