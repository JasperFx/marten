using System.Collections.Generic;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class ForEachEventFrame: Frame
    {
        private readonly IReadOnlyList<Frame> _inner;

        public ForEachEventFrame(IReadOnlyList<Frame> inner) : base(inner.Any(x => x.IsAsync))
        {
            _inner = inner;
            Event = new Variable(typeof(IEvent), "@event");
        }

        public string EventIteration { get; set; } = "slice.Events";

        public Variable Event { get; }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            return _inner.SelectMany(x => x.FindVariables(chain));
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.Write($"BLOCK:foreach (var @event in {EventIteration})");
            foreach (var frame in _inner)
            {
                frame.GenerateCode(method, writer);
            }
            writer.FinishBlock();

            Next?.GenerateCode(method, writer);
        }


    }
}
