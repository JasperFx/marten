using LamarCodeGeneration;
using LamarCodeGeneration.Model;
using NpgsqlTypes;

namespace Marten.Schema.Arguments
{
    /// <summary>
    /// Represents function argument.
    /// </summary>
    public interface IFunctionArgument
    {
        /// <summary>
        /// Gets arg name.
        /// </summary>
        string Arg { get; }

        /// <summary>
        /// Gets postgres type name.
        /// </summary>
        string PostgresType { get; }

        /// <summary>
        /// Gets associated column name.
        /// </summary>
        string Column { get; }

        /// <summary>
        /// Gets postgres driver type.
        /// </summary>
        NpgsqlDbType DbType { get; }

        void GenerateCodeToModifyDocument(
            GeneratedMethod method,
            GeneratedType type,
            int i,
            Argument parameters,
            DocumentMapping mapping,
            StoreOptions options);

        void GenerateCodeToSetDbParameterValue(
            GeneratedMethod method,
            GeneratedType type,
            int i,
            Argument parameters,
            DocumentMapping mapping,
            StoreOptions options);

        void GenerateBulkWriterCode(
            GeneratedType type,
            GeneratedMethod load,
            DocumentMapping mapping);

        void GenerateBulkWriterCodeAsync(
            GeneratedType type,
            GeneratedMethod load,
            DocumentMapping mapping);
    }

    /// <summary>
    /// Provides function argument.
    /// </summary>
    public interface IFunctionArgumentProvider
    {
        /// <summary>
        /// Gets function argument.
        /// </summary>
        IFunctionArgument ToArgument();
    }
}
