using System;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Internal;

// #5337: this used to be a byte-identical local copy of what is now
// JasperFx.Core.Reflection.WalkReferencedAssemblies. Nothing in the Marten solution
// uses it any longer; the shell stays (delegating, [Obsolete]) only because the type
// is public and a third-party consumer could reference it.
[Obsolete("Use JasperFx.Core.Reflection.WalkReferencedAssemblies instead. This duplicate will be removed in Marten 10.")]
public static class WalkReferencedAssemblies
{
    public static IEnumerable<Assembly> ForTypes(params Type[] types)
    {
        return JasperFx.Core.Reflection.WalkReferencedAssemblies.ForTypes(types);
    }
}
