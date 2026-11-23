using System;
using System.Text.Json.Serialization;
using Marten;
using Marten.Services;

namespace CoreTests.JsonSerializationSamples;

// Compile-only samples for docs/configuration/json.md. Nothing here runs as a test; the
// source-generated context is covered by stj_source_generation_context_tests.cs.

public class MyDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; }
}

public class AnotherDocument
{
    public Guid Id { get; set; }
    public int Count { get; set; }
}

#region sample_stj_source_generated_context
[JsonSerializable(typeof(MyDocument))]
[JsonSerializable(typeof(AnotherDocument))]
internal partial class MyMartenJsonContext : JsonSerializerContext;
#endregion

public class json_serialization_documentation_samples
{
    #region sample_use_type_info_resolver
    public void use_type_info_resolver(string connectionString)
    {
        var serializer = new SystemTextJsonSerializer();
        serializer.UseTypeInfoResolver(MyMartenJsonContext.Default);

        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(connectionString);
            opts.Serializer(serializer);
        });
    }
    #endregion

    #region sample_replace_the_serializer
    public void replace_the_serializer(ISerializer mySerializer)
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection("the connection string");

            // Replace Marten's ISerializer with your own
            opts.Serializer(mySerializer);
        });
    }
    #endregion
}
