using System;
using JasperFx.Core;

namespace Marten.Events.CodeGeneration;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property)]
public class MartenIgnoreAttribute: JasperFxIgnoreAttribute
{
}
