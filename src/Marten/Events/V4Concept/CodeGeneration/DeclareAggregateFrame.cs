using System;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class DeclareAggregateFrame: SyncFrame
    {

        public DeclareAggregateFrame(Type aggregateType)
        {
            Variable = new Variable(aggregateType, this);
        }

        public Variable Variable { get; }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"{Variable.VariableType.FullNameInCode()} {Variable.Usage} = null;");
            Next?.GenerateCode(method, writer);
        }
    }
}
