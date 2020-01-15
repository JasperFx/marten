using System;

namespace Marten.Schema
{
    /// <summary>
    /// Use to designate an Id property or field on a document type that doesn't follow the
    /// id/Id naming convention
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class IdentityAttribute: MartenAttribute
    {
    }
}
