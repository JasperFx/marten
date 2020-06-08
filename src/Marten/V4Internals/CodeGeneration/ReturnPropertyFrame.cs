using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.V4Internals
{
    public class ReturnPropertyFrame: SyncFrame
    {
        private readonly Type _documentType;
        private readonly MemberInfo _member;
        private Variable _document;

        public ReturnPropertyFrame(Type documentType, MemberInfo member)
        {
            _documentType = documentType;
            _member = member;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"return {_document.Usage}.{_member.Name};");
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _document = chain.FindVariable(_documentType);
            yield return _document;
        }
    }

    // TODO -- this should be in LamarCodeGeneration
    public class ReturnTaskCompleted: Frame
    {
        public ReturnTaskCompleted() : base(true)
        {

        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"return {typeof(Task).FullNameInCode()}.{nameof(Task.CompletedTask)};");
        }
    }
}
