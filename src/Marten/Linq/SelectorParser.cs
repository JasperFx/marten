using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Baseline;
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

        public ISelector<T> ToSelector<T>(IDocumentMapping mapping)
        {
            if (_selector != null) return _selector.As<ISelector<T>>();

            return _target == null 
                ? new SingleFieldSelector<T>(mapping, _currentField.Members.Reverse().ToArray()) 
                : _target.ToSelector<T>(mapping);
        }

        private static string _methodName = nameof(JsonExtensions.AsJson);
        private JsonSelector _selector;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.Name == _methodName && node.Method.DeclaringType.Equals(typeof (JsonExtensions)))
            {
                _selector = new JsonSelector();
                return null;
            }

            return base.VisitMethodCall(node);
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