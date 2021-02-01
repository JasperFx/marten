using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.Events.CodeGeneration
{
    internal class DefaultAggregateConstruction: SyncFrame
    {
        private readonly Type _returnType;
        private Variable _event;
        private readonly Setter _setter;
        private readonly ConstructorInfo _constructor;

        public DefaultAggregateConstruction(Type returnType, GeneratedType generatedType)
        {
            _returnType = returnType;

            _constructor = returnType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

            if (_constructor != null && !_constructor.IsPublic)
            {
                var ctor = Expression.New(_constructor);
                var lambda = Expression.Lambda(ctor);
                var func = lambda.Compile();
                _setter = new Setter(func.GetType(), "AggregateBuilder")
                {
                    InitialValue = func
                };
                generatedType.Setters.Add(_setter);
            }

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

            if (_constructor == null)
            {
                writer.WriteLine($"throw new {typeof(InvalidOperationException).FullNameInCode()}(\"There is no default constructor for {_returnType.FullNameInCode()}\");");
            }
            else if (_setter != null)
            {
                writer.WriteLine("return AggregateBuilder();");
            }
            else
            {
                writer.WriteLine($"return new {_returnType.FullNameInCode()}();");
            }

            IfStyle.Close(writer);

            Next?.GenerateCode(method, writer);
        }
    }
}
