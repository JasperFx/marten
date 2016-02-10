using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Schema;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public class FindMembers : RelinqExpressionVisitor
    {
        private IHaveMembers MembersContainer { get; set; } = new MembersContainer();
        public IList<MemberInfo> Members => MembersContainer.Members;

        public IField GetField(DocumentMapping mapping)
        {
            return MembersContainer.GetField(mapping);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType != ExpressionType.Modulo) return base.VisitBinary(node);
            var moduloValue = 1;
            var moduloValueExpression = node.Right as ConstantExpression;
            if (moduloValueExpression != null) moduloValue = (int)moduloValueExpression.Value;
            MembersContainer = new ModuloMembersContainer(MembersContainer.Members,moduloValue);
            return base.VisitBinary(node);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            MembersContainer.Prepend(node.Member);

            return base.VisitMember(node);
        }
    }

    abstract class IHaveMembers
    {
        public IList<MemberInfo> Members { get; protected set; }
        public void Prepend(MemberInfo member)
        {
            Members.Insert(0, member);
        }
        public abstract IField GetField(DocumentMapping mapping);

        protected IHaveMembers(IList<MemberInfo> members)
        {
            Members = members;
        }
    }

    class MembersContainer : IHaveMembers
    {
        public MembersContainer() : this(new List<MemberInfo>())
        { }
        public MembersContainer(IList<MemberInfo> members) : base(members)
        { }

        public override IField GetField(DocumentMapping mapping)
        {
            return mapping.FieldFor(Members);
        }
    }

    class ModuloMembersContainer : IHaveMembers
    {
        public int ModuloValue { get; }

        public ModuloMembersContainer(IList<MemberInfo> members, int moduloValue) : base(members)
        {
            ModuloValue = moduloValue;
        }

        public override IField GetField(DocumentMapping mapping)
        {
            return mapping.FieldFor(Members, ModuloValue);
        }
    }
}