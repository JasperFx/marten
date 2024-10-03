using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Core.TypeScanning;
using Marten.Linq;
using Marten.Schema;

namespace Marten;

public partial class StoreOptions
{
    internal List<Type> CompiledQueryTypes => _compiledQueryTypes;

    /// <summary>
    ///     Force Marten to create document mappings for type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void RegisterDocumentType<T>()
    {
        RegisterDocumentType(typeof(T));
    }

    /// <summary>
    ///     Force Marten to create a document mapping for the document type
    /// </summary>
    /// <param name="documentType"></param>
    public void RegisterDocumentType(Type documentType)
    {
        Storage.RegisterDocumentType(documentType);
    }

    /// <summary>
    /// Scan the application assembly and any other supplied assemblies to
    /// discover compiled query types, event types by supplied filters, or
    /// any type decorated with a MartenAttribute
    /// </summary>
    /// <param name="configure">Configure the automatic type discovery</param>
    public void AutoRegister(Action<MartenAssemblyScanner> configure)
    {
        var scanner = new MartenAssemblyScanner();
        configure(scanner);
        scanner.Scan(this);
    }

    /// <summary>
    /// Scan the application assembly to
    /// discover compiled query types or
    /// any type decorated with a MartenAttribute
    /// </summary>
    public void AutoRegister()
    {
        var scanner = new MartenAssemblyScanner();
        scanner.Scan(this);
    }

    /// <summary>
    ///     Force Marten to create document mappings for all the given document types
    /// </summary>
    /// <param name="documentTypes"></param>
    public void RegisterDocumentTypes(IEnumerable<Type> documentTypes)
    {
        documentTypes.Each(RegisterDocumentType);
    }

    /// <summary>
    ///     Register a compiled query type for the "generate ahead" code generation strategy
    /// </summary>
    /// <param name="queryType"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void RegisterCompiledQueryType(Type queryType)
    {
        if (!queryType.Closes(typeof(ICompiledQuery<,>)))
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                $"{queryType.FullNameInCode()} is not a valid Marten compiled query type");
        }

        if (!queryType.HasDefaultConstructor())
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                "Sorry, but Marten requires a no-arg constructor on compiled query types in order to opt into the 'code ahead' generation model.");
        }

        CompiledQueryTypes.Fill(queryType);
    }

    public class MartenAssemblyScanner
{
    private readonly List<Assembly> _assemblies = new();
    private readonly List<Func<Type, bool>> _eventMatchers = new();

    /// <summary>
    /// Which assemblies in addition to the StoreOptions.ApplicationAssembly should
    /// be scanned for potential Marten registrations?
    /// </summary>
    /// <param name="assembly"></param>
    public void Assembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _assemblies.Add(assembly);
    }

    /// <summary>
    /// Use your own logic to tell Marten which types are event types!
    /// </summary>
    /// <param name="filter"></param>
    public void EventsMatch(Func<Type, bool> filter)
    {
        _eventMatchers.Add(filter);
    }

    /// <summary>
    /// Find and register any event types that implement the supplied
    /// interface or base class
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void EventsImplement<T>()
    {
        EventsImplement(typeof(T));
    }

    /// <summary>
    /// Find and register any event types that implement the supplied
    /// interface or base class
    /// </summary>
    /// <param name="type"></param>
    public void EventsImplement(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        _eventMatchers.Add(t => t.CanBeCastTo(type));
    }

    internal void Scan(StoreOptions options)
    {
        var assemblies = _assemblies.Union([options.ApplicationAssembly]).ToArray();
        var publicTypes =
            TypeRepository.FindTypes(assemblies, TypeClassification.Concretes | TypeClassification.Closed, type => type.IsPublic || type.IsNestedPublic)
                .ToArray();

        // Find any compiled queries
        var compiledQueries = publicTypes.Where(x => x.CanBeCastTo<ICompiledQueryMarker>());
        foreach (var compiledQueryType in compiledQueries)
        {
            options.RegisterCompiledQueryType(compiledQueryType);
        }

        // Find any event types
        if (_eventMatchers.Any())
        {
            foreach (var eventType in publicTypes.Where(x => _eventMatchers.Any(filter => filter(x))))
            {
                options.Events.AddEventType(eventType);
            }
        }

        // Find any types marked with [MartenAttribute]
        foreach (var publicType in publicTypes)
        {
            if (publicType.TryGetAttribute<MartenAttribute>(out var att))
            {
                att.Register(publicType, options);
            }
        }
    }
}
}
