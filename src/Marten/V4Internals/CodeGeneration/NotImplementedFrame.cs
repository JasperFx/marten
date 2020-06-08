using System;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;

namespace Marten.V4Internals
{
    public class NotImplementedFrame: SyncFrame
    {
        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"throw new {typeof(NotImplementedException).FullNameInCode()}();");
        }
    }
}
