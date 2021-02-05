using System.Collections.Generic;
using System.Linq;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.CodeGeneration
{
    internal class ForEachEventFrame: Frame
    {
        private readonly EventTypePatternMatchFrame _inner;

        public ForEachEventFrame(EventTypePatternMatchFrame inner) : base(inner.IsAsync)
        {
            _inner = inner;
            Event = new Variable(typeof(IEvent), "@event");
        }

        public string EventIteration { get; set; } = "slice.Events";

        public Variable Event { get; }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            return _inner.FindVariables(chain);
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.Write($"BLOCK:foreach (var @event in {EventIteration})");
            _inner.GenerateCode(method, writer);
            writer.FinishBlock();

            Next?.GenerateCode(method, writer);
        }


    }
}
