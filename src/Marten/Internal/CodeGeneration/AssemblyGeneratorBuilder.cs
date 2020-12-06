using System;
using LamarCompiler;
using Marten.Internal.Storage;

namespace Marten.Internal.CodeGeneration
{
    internal class AssemblyGeneratorBuilder
    {
        private readonly AssemblyGenerator _compiler
            = new AssemblyGenerator {AssemblyName = MartenGenerated.AssemblyName};
        public static AssemblyGeneratorBuilder Create()
            => new AssemblyGeneratorBuilder();

        private AssemblyGeneratorBuilder(){}

        public AssemblyGeneratorBuilder ReferencingMartenAssembly()
            => ReferencingAssemblyWith(typeof(IDocumentStorage<>));


        public AssemblyGeneratorBuilder ReferencingAssemblyWith<T>()
            => ReferencingAssemblyWith(typeof(T));

        public AssemblyGeneratorBuilder ReferencingAssemblyWith(Type type)
        {
            _compiler.ReferenceAssembly(type.Assembly);

            return this;
        }

        public AssemblyGenerator Build() => _compiler;
    }

    public static class MartenGenerated
    {
        public const string AssemblyName = "Marten.Generated";
    }
}
