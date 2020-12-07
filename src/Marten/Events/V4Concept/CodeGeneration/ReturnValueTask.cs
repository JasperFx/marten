using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.V4Concept.CodeGeneration
{
    public class ReturnValueTask: SyncFrame
    {
        private readonly Type _variableType;
        private Variable _returnValue;

        public ReturnValueTask(Type variableType)
        {
            _variableType = variableType;
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _returnValue = chain.FindVariable(_variableType);
            yield return _returnValue;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine(
                $"return new {typeof(ValueTask).FullNameInCode()}<{_variableType.FullNameInCode()}>({_returnValue.Usage});");
        }
    }
}
