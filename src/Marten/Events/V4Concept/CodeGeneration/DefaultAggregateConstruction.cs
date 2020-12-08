using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Util;

namespace Marten.Events.V4Concept.CodeGeneration
{
    internal class DefaultAggregateConstruction: SyncFrame
    {
        private readonly Type _returnType;
        private Variable _event;

        public DefaultAggregateConstruction(Type returnType)
        {
            _returnType = returnType;
        }

        public IfStyle IfStyle { get; set; } = IfStyle.Else;

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _event = chain.FindVariable(typeof(IEvent));
            yield return _event;

        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            IfStyle.Open(writer, null);

            writer.WriteLine(_returnType.HasDefaultConstructor()
                ? $"return new {_returnType.FullNameInCode()}();"
                : $"throw new {typeof(InvalidOperationException).FullNameInCode()}(\"There is no default constructor for {_returnType.FullNameInCode()}\");");

            IfStyle.Close(writer);

            Next?.GenerateCode(method, writer);
        }
    }
}
