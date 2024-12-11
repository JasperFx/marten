using System;
using System.Collections.Generic;
using System.Reflection;
using Marten.Internal;
using Marten.Linq.SqlGeneration;
using Microsoft.FSharp.Core;

namespace Marten.Linq.Members;

public class FSharpOptionValueTypeMember<TOption> : ValueTypeMember<FSharpOption<TOption>, TOption>, IComparableMember
{
    public FSharpOptionValueTypeMember(IQueryableMember parent, Casing casing, MemberInfo member, ValueTypeInfo valueTypeInfo) : base(parent, casing, member, valueTypeInfo)
    {

    }

}
