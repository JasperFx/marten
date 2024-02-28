using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core.Reflection;

namespace Marten.Events.CodeGeneration;

internal class EventTypePatternMatchFrame: Frame
{
    private readonly List<EventProcessingFrame> _inner;
    private Variable _event;

    public EventTypePatternMatchFrame(List<EventProcessingFrame> frames) : base(frames.Any(x => x.IsAsync))
    {
        _inner = frames;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        if (_inner.Any())
        {
            writer.Write($"BLOCK:switch ({_event.Usage})");
            var sortedEventFrames = SortByEventTypeHierarchy(_inner).ToArray();
            if (sortedEventFrames.Length != _inner.Count)
            {
                throw new InvalidOperationException("Event types were lost during the sorting");
            }

            foreach (var frame in sortedEventFrames)
            {
                frame.GenerateCode(method, writer);
            }

            writer.FinishBlock();
        }

        Next?.GenerateCode(method, writer);
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        _event = chain.FindVariable(typeof(IEvent));

        yield return _event;

        foreach (var variable in _inner.SelectMany(x => x.FindVariables(chain)))
            yield return variable;
    }

    /// <summary>
    /// Sort event processing frames by event type hierarchy
    /// </summary>
    /// <param name="frames"></param>
    /// <returns></returns>

    internal static IEnumerable<EventProcessingFrame> SortByEventTypeHierarchy(IEnumerable<EventProcessingFrame> frames)
    {
        var framesReference = frames.ToDictionary(p => p.EventType);
        return TypeSorter.SortByHierarchy(framesReference.Keys)
            .Where(p => framesReference.ContainsKey(p))
            .Select(p => framesReference[p]);
    }

    private static class TypeSorter
    {
        private static ConcurrentDictionary<Type,List<Type>> Graph { get; } = new();

        public static IEnumerable<Type> SortByHierarchy(ICollection<Type> types)
        {
            var typesSet = new HashSet<Type>(types);
            PopulateGraph(types);

            var visited = new HashSet<Type>();
            var sorted = new Stack<Type>();

            // Topological sort
            foreach (var type in typesSet.OrderByDescending(type => type.FullName)) //Orders by name at top-level
            {
                VisitGraph(type, visited, sorted);
            }

            return sorted.Where(typesSet.Contains);
        }

        private static void VisitGraph(Type type, HashSet<Type> visited, Stack<Type> sorted)
        {
            //Traverse the graph depth first recursively
            if (visited.Contains(type)) return;
            visited.Add(type);

            foreach (var child in Graph[type])
            {
                VisitGraph(child, visited, sorted);
            }

            sorted.Push(type);
        }

        private static void PopulateGraph(ICollection<Type> types)
        {
            foreach (var type in types)
            {
                AddTypeHierarchy(type);
            }
        }

        private static void AddTypeHierarchy(Type type)
        {
            if(Graph.ContainsKey(type))
                return;

            Graph.TryAdd(type, new List<Type>());
            
            // Add base types to the graph
            var baseType = type.BaseType;
            if (baseType != null)
            {
                Graph[type].Add(baseType);
                AddTypeHierarchy(baseType);
            }

            // Add interfaces to the graph
            foreach (var interfaceType in type.GetInterfaces())
            {
                Graph[type].Add(interfaceType);
                AddTypeHierarchy(interfaceType);
            }
        }
    }
}
