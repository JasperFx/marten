using LamarCodeGeneration;

namespace Marten.Events.Schema
{
    /// <summary>
    /// This interface is used by the event store code generation to build the IEventStorage
    /// </summary>
    internal interface IEventTableColumn
    {
        /// <summary>
        /// Generate the synchronous IEventSelector code for this event table column
        /// </summary>
        /// <param name="method"></param>
        /// <param name="graph"></param>
        /// <param name="index"></param>
        public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index);

        /// <summary>
        /// Generate the asynchronous IEventSelector code for this event table column
        /// </summary>
        /// <param name="method"></param>
        /// <param name="graph"></param>
        /// <param name="index"></param>
        public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index);

        /// <summary>
        /// Generate code for this column to capture the NpgsqlParameter value that should
        /// be persisted when appending an event to the events table
        /// </summary>
        /// <param name="method"></param>
        /// <param name="graph"></param>
        /// <param name="index"></param>
        public void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index);

        /// <summary>
        /// Column name
        /// </summary>
        string Name { get; }
    }
}
