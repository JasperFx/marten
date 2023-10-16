using System;
using System.Collections.Generic;
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

    public EventTypePatternMatchFrame(List<EventProcessingFrame> frames): base(frames.Any(x => x.IsAsync))
    {
        _inner = frames;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        if (_inner.Any())
        {
            writer.Write($"BLOCK:switch ({_event.Usage})");
            foreach (var frame in SortByEventTypeHierarchy(_inner))
                frame.GenerateCode(method, writer);

            writer.FinishBlock();
        }

        Next?.GenerateCode(method, writer);
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        _event = chain.FindVariable(typeof(IEvent));

        yield return _event;

        foreach (var variable in _inner.SelectMany(x => x.FindVariables(chain))) yield return variable;
    }

    /// <summary>
    /// Sort event processing frames by event type hierarchy
    /// </summary>
    /// <param name="frames"></param>
    /// <returns></returns>
    internal static IEnumerable<EventProcessingFrame> SortByEventTypeHierarchy(IEnumerable<EventProcessingFrame> frames)
    {
        return new SortedSet<EventProcessingFrame>(frames, new EventTypeComparer());
    }

    /// <summary>
    /// Sort frames by event type hierarchy
    /// <remarks>Comparer is not safe to use outside of intended purpose</remarks>
    /// </summary>
    private class EventTypeComparer: IComparer<EventProcessingFrame>
    {
        public int Compare(EventProcessingFrame x, EventProcessingFrame y)
        {
            if (x.EventType.CanBeCastTo(y.EventType))
                return -1;

            if (y.EventType.CanBeCastTo(x.EventType))
                return 1;

            return 0;
        }
    }
}
