#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;

namespace Marten.Schema.Identity;

/// <summary>
///     Identity generation strategy
/// </summary>
public interface IIdGeneration
{
    /// <summary>
    ///     Does this strategy require number sequences
    /// </summary>
    bool IsNumeric { get; }

    /// <summary>
    ///     This method must be implemented to build and set the identity on
    ///     a document
    /// </summary>
    /// <param name="method"></param>
    /// <param name="mapping"></param>
    void GenerateCode(GeneratedMethod method, DocumentMapping mapping);
}
