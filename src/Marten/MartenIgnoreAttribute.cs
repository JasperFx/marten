using System;
using JasperFx.Core;

namespace Marten;

/// <summary>
/// Just tells Marten to ignore whatever method, field, or property in any conventions that is
/// decorated with this attribute
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
public class MartenIgnoreAttribute: JasperFxIgnoreAttribute
{
}
