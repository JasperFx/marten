using System;
using System.Diagnostics.CodeAnalysis;
using JasperFx.Core.Exceptions;
using Npgsql;

namespace Marten.Exceptions;
#nullable enable

/// <summary>
///     Informs that Postgres connection string is invalid
/// </summary>
public class InvalidConnectionStringException: MartenCommandException
{
    public InvalidConnectionStringException(NpgsqlCommand? command, ArgumentException innerException): base(command,
        innerException)
    {
    }

    public InvalidConnectionStringException(NpgsqlCommand? command, ArgumentException innerException, string prefix):
        base(
            command, innerException, prefix)
    {
    }
}

internal class InvalidConnectionStreamExceptionTransform: IExceptionTransform
{
#pragma warning disable CS8767 // Nullability needs to be fixed in Jasper.Core
    public bool TryTransform(Exception original, out Exception? transformed)
#pragma warning restore CS8767 // Nullability needs to be fixed in Jasper.Core
    {
        if (original is ArgumentException e)
        {
            var matchesExceptionPattern =
                e.Message.Contains(
                    "Format of the initialization string does not conform to specification")
                && e.Source == "System.Data.Common";

            if (matchesExceptionPattern)
            {
                var command = e.Data.Contains(nameof(NpgsqlCommand))
                    ? e.Data[nameof(NpgsqlCommand)] as NpgsqlCommand
                    : null;

                transformed =
                    new InvalidConnectionStringException(command, e, "Invalid connection string.");

                return true;
            }
        }

        transformed = null;
        return false;
    }
}
