using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Marten.Codegen
{
    public class AssemblyGenerator
    {
        private readonly IList<MetadataReference> _references = new List<MetadataReference>();

        public AssemblyGenerator()
        {
            ReferenceAssemblyContainingType<object>();
            ReferenceAssembly(typeof (Enumerable).Assembly);
        }

        public StringWriter Text { get; } = new StringWriter();

        public void ReferenceAssembly(Assembly assembly)
        {
            _references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        public void ReferenceAssemblyContainingType<T>()
        {
            ReferenceAssembly(typeof (T).Assembly);
        }

        public Assembly Generate()
        {
            var assemblyName = Path.GetRandomFileName();
            var syntaxTree = CSharpSyntaxTree.ParseText(Text.ToString());

            var references = _references.ToArray();
            var compilation = CSharpCompilation.Create(assemblyName, new[] {syntaxTree}, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));


            using (var stream = new MemoryStream())
            {
                var result = compilation.Emit(stream);

                if (!result.Success)
                {
                    var failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);


                    var message = failures.Select(x => $"{x.Id}: {x.GetMessage()}").Join("\n");
                    throw new InvalidOperationException("Compilation failures!\n\n" + message);
                }

                stream.Seek(0, SeekOrigin.Begin);
                return Assembly.Load(stream.ToArray());
            }
        }
    }
}