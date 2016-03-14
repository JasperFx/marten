using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Baseline.Reflection;
using Marten.Schema;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{
    public class SelectorParser : RelinqExpressionVisitor
    {
        private SelectedField _currentField = new SelectedField();
        private TargetObject _target;

        protected override Expression VisitNew(NewExpression expression)
        {
            if (_target == null)
            {
                _target = new TargetObject(expression.Type);
                if (expression.Type.HasAttribute<CompilerGeneratedAttribute>())
                {
                    // it's anonymous, and the rules are different
                    var parameters = expression.Constructor.GetParameters();

                    for (var i = 0; i < parameters.Length; i++)
                    {
                        var prop = expression.Type.GetProperty(parameters[i].Name);
                        _currentField = _target.StartBinding(prop);
                        Visit(expression.Arguments[i]);
                    }

                    return null;

                }
            }

            return base.VisitNew(expression);
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _currentField.Members.Add(node.Member);
            return base.VisitMember(node);
        }

        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            _currentField = _target.StartBinding(node.Member);

            return base.VisitMemberBinding(node);
        }

        public ISelector<T> ToSelector<T>()
        {
            return _target == null 
                ? new SingleFieldSelector<T>(_currentField.Members.Reverse().ToArray()) 
                : _target.ToSelector<T>();
        }
    }

    public class SetterBinding
    {
        public MemberInfo Setter { get; }
        public SelectedField Field { get; } = new SelectedField();

        public SetterBinding(MemberInfo setter)
        {
            Setter = setter;

        }

        public string ToJsonBuildObjectPair(IDocumentMapping mapping)
        {
            var locator = mapping.FieldFor(Field.Members).SqlLocator;
            return $"'{Setter.Name}', {locator}";
        }
    }

    public class SelectedField
    {
        public readonly IList<MemberInfo> Members = new List<MemberInfo>();


    }
}