using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Baseline;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Transforms;
using Remotion.Linq;
using Remotion.Linq.Parsing;

namespace Marten.Linq
{

    public enum SelectionType
    {
        AsJson,
        WholeDoc,
        Select,
        SingleField,
        TransformToJson,
        TransformTo
    }


    public class SelectorParser : RelinqExpressionVisitor
    {
        private SelectedField _currentField = new SelectedField();
        private SelectionType _selectionType = SelectionType.WholeDoc;
        private TargetObject _target;
        private string _transformName;


        public SelectorParser(QueryModel query)
        {
            if (query.HasOperator<AsJsonResultOperator>())
            {
                _selectionType = SelectionType.AsJson;
            }



            if (query.SelectClause.Selector is MethodCallExpression)
            {
                var method = query.SelectClause.Selector.As<MethodCallExpression>().Method;
                _selectionType = DetermineSelectionType(method);
            }
        }

        public SelectionType DetermineSelectionType(MethodInfo method)
        {
            if (method.DeclaringType == typeof(CompiledQueryExtensions) &&
                method.Name == nameof(CompiledQueryExtensions.AsJson))
            {
                return SelectionType.AsJson;
            }


            if (method.DeclaringType == typeof(TransformExtensions))
            {
                if (method.Name == nameof(TransformExtensions.TransformToJson))
                {
                    return SelectionType.TransformToJson;
                }

                if (method.Name == nameof(TransformExtensions.TransformTo))
                {
                    return SelectionType.TransformTo;
                }
            }

            return SelectionType.WholeDoc;
        }

        protected override Expression VisitNew(NewExpression expression)
        {
            if (_target == null)
            {
                _target = new TargetObject(expression.Type);
                if (expression.Type.GetTypeInfo().HasAttribute<CompilerGeneratedAttribute>())
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

        public ISelector<T> ToSelector<T>(IDocumentSchema schema, IQueryableDocument mapping)
        {
            if (_selectionType == SelectionType.AsJson && _target == null) return new JsonSelector().As<ISelector<T>>();

            if (_selectionType == SelectionType.AsJson && _target != null) return _target.ToJsonSelector<T>(mapping);


            if (_selectionType == SelectionType.TransformToJson)
            {
                var transform = schema.TransformFor(_transformName);
                return new TransformToJsonSelector(transform, mapping).As<ISelector<T>>();
            }

            if (_selectionType == SelectionType.TransformTo)
            {
                var transform = schema.TransformFor(_transformName);

                return new TransformToTypeSelector<T>(transform, mapping );
            }

            if (_target == null || _target.Type != typeof(T))
            {
                return new SingleFieldSelector<T>(mapping, _currentField.Members.Reverse().ToArray());
            }

            return _target.ToSelector<T>(mapping);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var method = node.Method;
            var selectionType = DetermineSelectionType(method);

            if (selectionType == SelectionType.AsJson)
            {
                _selectionType = SelectionType.AsJson;

                node.Arguments.Each(arg => Visit(arg));

                return null;
            }

            if (selectionType == SelectionType.TransformToJson || selectionType == SelectionType.TransformTo)
            {
                var transformName = node.Arguments.Last() as ConstantExpression;

                if (transformName != null)
                {
                    _transformName = transformName.Value.As<string>();
                }
            }

            return base.VisitMethodCall(node);
        }

    }

    public class SetterBinding
    {
        public SetterBinding(MemberInfo setter)
        {
            Setter = setter;
        }

        public MemberInfo Setter { get; }
        public SelectedField Field { get; } = new SelectedField();

        public string ToJsonBuildObjectPair(IQueryableDocument mapping)
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