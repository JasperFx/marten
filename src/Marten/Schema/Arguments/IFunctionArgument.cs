using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    /// <summary>
    /// Represents function argument.
    /// </summary>
    public interface IFunctionArgument
    {
        string Arg { get; }

        string PostgresType { get; }

        string Column { get; }

        NpgsqlDbType DbType { get; }

        string ArgumentDeclaration();
    }

    /// <summary>
    /// Provides function argument.
    /// </summary>
    public interface IFunctionArgumentProvider
    {
        /// <summary>
        /// Gets function argument.
        /// </summary>
        UpsertArgument ToArgument();
    }
}
