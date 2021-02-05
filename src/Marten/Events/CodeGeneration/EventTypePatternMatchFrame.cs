using System;
using System.Collections.Generic;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.CodeGeneration
{
    internal class EventTypePatternMatchFrame : Frame
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
                foreach (var frame in _inner)
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
            {
                yield return variable;
            }
        }
    }
}
