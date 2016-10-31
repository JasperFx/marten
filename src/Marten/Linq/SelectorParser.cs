using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Baseline;
using Baseline.Reflection;
using Marten.Events;
using Marten.Linq.Model;
using Marten.Schema;
using Marten.Services.Includes;
using Marten.Transforms;
using Remotion.Linq;
using Remotion.Linq.Clauses.ResultOperators;
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
        public static ISelector<T> ChooseSelector<T>(IDocumentSchema schema, IQueryableDocument mapping, QueryModel query, SelectManyQuery subQuery, IIncludeJoin[] joins)
        {
            // I'm so ashamed of this hack, but "simplest thing that works"
            if (typeof(T) == typeof(IEvent))
            {
                return mapping.As<EventQueryMapping>().Selector.As<ISelector<T>>();
            }

            var selectable = query.AllResultOperators().OfType<ISelectableOperator>().FirstOrDefault();
            if (selectable != null)
            {
                return selectable.BuildSelector<T>(schema, mapping);
            }


            if (subQuery != null)
            {
                return subQuery.ToSelector<T>(schema.StoreOptions.Serializer(), joins);
            }

            if (query.SelectClause.Selector.Type == query.SourceType())
            {
                if (typeof(T) == typeof(string))
                {
                    return (ISelector<T>)new JsonSelector();
                }

                if (typeof(T) != query.SourceType())
                {
                    // TODO -- going to have to come back to this one.
                    // think this is related to hierarchical documents
                    return null;
                }

                var resolver = schema.ResolverFor<T>();

                return new WholeDocumentSelector<T>(mapping, resolver);
            }


            var visitor = new SelectorParser(query);
            visitor.Visit(query.SelectClause.Selector);

            return visitor.ToSelector<T>(schema, mapping);
        }



        private SelectedField _currentField = new SelectedField();
        private SelectionType _selectionType = SelectionType.WholeDoc;
        private TargetObject _target;
        private string _transformName;
        private readonly bool _distinct;


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

            if (query.HasOperator<DistinctResultOperator>())
            {
                _distinct = true;
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
                return new SingleFieldSelector<T>(mapping, _currentField.Members.Reverse().ToArray(), _distinct);
            }

            return _target.ToSelector<T>(mapping, _distinct);
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
            var locator = mapping.FieldFor(Field.Members).SelectionLocator;


            return $"'{Setter.Name}', {locator}";
        }
    }

    public class SelectedField
    {
        public readonly IList<MemberInfo> Members = new List<MemberInfo>();
    }
}