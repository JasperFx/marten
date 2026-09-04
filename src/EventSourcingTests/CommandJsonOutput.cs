#nullable enable
using System;
using System.Text;
using System.Text.Json;

namespace EventSourcingTests;

/// <summary>
/// Shared stdout-parsing helper for the CLI end-to-end tests (<c>event-query</c> /
/// <c>stream-query</c>). Console.Out is process-global, so while a test has it redirected an
/// unrelated test running in parallel can interleave its own writes into the captured text —
/// anchoring on "first '{' to end of output" then breaks the JSON parse (seen as a 1-in-a-full-run
/// flake of event_query_command_end_to_end). This extracts the command's report robustly: try each
/// '{' candidate, parse exactly ONE JSON value there (ignoring whatever follows), and accept the
/// first object that carries the report signature.
/// </summary>
internal static class CommandJsonOutput
{
    public static JsonDocument ExtractReport(string consoleOutput)
    {
        var bytes = Encoding.UTF8.GetBytes(consoleOutput);

        for (var i = Array.IndexOf(bytes, (byte)'{'); i >= 0; i = Array.IndexOf(bytes, (byte)'{', i + 1))
        {
            try
            {
                var reader = new Utf8JsonReader(bytes.AsSpan(i));
                if (JsonDocument.TryParseValue(ref reader, out var document))
                {
                    // Both command reports always carry pageSize; a foreign JSON blob from another
                    // test's output almost certainly does not.
                    if (document.RootElement.ValueKind == JsonValueKind.Object &&
                        document.RootElement.TryGetProperty("pageSize", out _))
                    {
                        return document;
                    }

                    document.Dispose();
                }
            }
            catch (JsonException)
            {
                // Not a JSON value at this brace — keep scanning.
            }
        }

        throw new InvalidOperationException(
            $"Expected a JSON command report on stdout, but could not find one in: {consoleOutput}");
    }
}
