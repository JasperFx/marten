using System;
using JasperFx.CodeGeneration;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;

namespace ModularConfigTests;

/// <summary>
/// Shared helpers for the modular-config test fixture. Each test calls
/// <see cref="AddBaselineMarten"/> to register Marten with a unique
/// per-test schema name so the suite can run in parallel without
/// cross-test interference.
/// </summary>
internal static class ConfigurationFixture
{
    /// <summary>
    /// Stable per-call schema name derived from a caller-provided prefix.
    /// Postgres identifiers cap at 63 bytes; trim accordingly.
    /// </summary>
    public static string UniqueSchemaName(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        var candidate = $"{prefix}_{suffix}";
        return candidate.Length > 50 ? candidate.Substring(0, 50) : candidate;
    }

    /// <summary>
    /// Standard AddMarten registration the test harness uses. Takes a
    /// per-test schema name so isolated runs don't trip over each other.
    /// </summary>
    public static void AddBaselineMarten(IServiceCollection services, string schemaName) =>
        services.AddMarten(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = schemaName;
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
        });
}
