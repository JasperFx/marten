using System;
using System.Linq;
using System.Reflection;
using FubuCore;
using Marten.Codegen;
using Marten.Testing;
using Marten.Util;

namespace Marten.Testing
{
}

public interface IMessageWriter
{
    void WriteMessages();
}

public class codegen_spike
{
    public void generate_a_small_class()
    {
        var code = @"
using System;
using Marten.Testing;


namespace RoslynGenerationSpike
{
    public class Writer : IMessageWriter
    {
        public void WriteMessages()
        {
            Console.WriteLine('What is this?');
            Console.WriteLine('Dynamic Code?');
            Console.WriteLine('Really?');
        }
    }
}
".Replace("'", "\"");


        // New helper for this mess called AssemblyGenerator
        var builder = new AssemblyGenerator();

        // Exposes a StringWriter for appending code
        builder.Text.WriteLine(code);

        // you might need to help it out w/ references
        builder.ReferenceAssembly(Assembly.GetExecutingAssembly());

        // Make the new Assembly
        var assembly = builder.Generate();

        // Do stuff with it
        var writerType = assembly.GetExportedTypes().Single();
        var writer = Activator.CreateInstance(writerType).As<IMessageWriter>();

        writer.WriteMessages();
        writer.WriteMessages();
        writer.WriteMessages();
        writer.WriteMessages();
        writer.WriteMessages();
        writer.WriteMessages();
        writer.WriteMessages();
    }
}