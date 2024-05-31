#nullable enable
using System;
using System.Reflection;

namespace Marten.Schema;

/// <summary>
///     Base type of an Attribute that can be extended to add per field/property
///     or per document type customization to the document storage
/// </summary>

#region sample_MartenAttribute

public abstract class MartenAttribute: Attribute
{
    /// <summary>
    ///     Customize Document storage at the document level
    /// </summary>
    /// <param name="mapping"></param>
    public virtual void Modify(DocumentMapping mapping) { }

    /// <summary>
    ///     Customize the Document storage for a single member
    /// </summary>
    /// <param name="mapping"></param>
    /// <param name="member"></param>
    public virtual void Modify(DocumentMapping mapping, MemberInfo member) { }

    /// <summary>
    /// When used with the automatic type discovery (assembly scanning), this will be called
    /// to make registrations to the Marten configuration with the type that this attribute
    /// decorates
    /// </summary>
    /// <param name="discoveredType"></param>
    /// <param name="options"></param>
    public virtual void Register(Type discoveredType, StoreOptions options){}
}

#endregion
