using System;
using System.Collections.Generic;
using LamarCodeGeneration;
#nullable enable
namespace Marten.Schema.Identity
{
    /// <summary>
    /// Identity generation strategy
    /// </summary>
    public interface IIdGeneration
    {
        /// <summary>
        /// What types are supported by this strategy? Example: string, or int/long, or Guid
        /// </summary>
        IEnumerable<Type> KeyTypes { get; }

        /// <summary>
        /// Does this strategy require HiLo sequences
        /// </summary>
        bool RequiresSequences { get; }

        /// <summary>
        /// This method must be implemented to build and set the identity on
        /// a document
        /// </summary>
        /// <param name="method"></param>
        /// <param name="mapping"></param>
        void GenerateCode(GeneratedMethod method, DocumentMapping mapping);
    }
}
