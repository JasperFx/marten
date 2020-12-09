using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using LamarCompiler;
using Marten.Events.V4Concept.CodeGeneration;
using Marten.Internal;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Storage;
using Marten.Schema;
using Marten.Storage;

namespace Marten.Events.V4Concept.Aggregation
{
    public abstract partial class V4AggregateProjection<T>: IAggregateProjection
    {
        internal IList<Type> DeleteEvents { get; } = new List<Type>();


    }


}
