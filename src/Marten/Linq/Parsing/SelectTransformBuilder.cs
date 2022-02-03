using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Fields;
using Marten.Linq.SqlProjection;
using Remotion.Linq.Parsing;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Parsing
{
    internal class SelectTransformBuilder : RelinqExpressionVisitor
    {
        private TargetObject _target;
        private BindingTarget _currentTarget;

        public SelectTransformBuilder(Expression clause, IFieldMapping fields, ISerializer serializer)
        {
            // ReSharper disable once VirtualMemberCallInConstructor
            Visit(@clause);
            SelectedFieldExpression = _target.ToSelectField(fields, serializer);
        }

        public string SelectedFieldExpression { get; }

        protected override Expression VisitNew(NewExpression expression)
        {
            if (_target != null)
                return base.VisitNew(expression);

            _target = new TargetObject(expression.Type);

            var parameters = expression.Constructor.GetParameters();

            for (var i = 0; i < parameters.Length; i++)
            {
                _currentTarget = _target.StartBinding(parameters[i].Name);
                Visit(expression.Arguments[i]);
            }

            return expression;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _currentTarget.AddMember(node.Member);
            return base.VisitMember(node);
        }

        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            _currentTarget = _target.StartBinding(node.Member.Name);

            return base.VisitMemberBinding(node);
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var fragment = SqlProjectionSqlFragment.TryParse(node);
            if (fragment == null)
            {
                throw new NotSupportedException(
                    $"Method {node.Method.DeclaringType?.FullName}.{node.Method.Name} is not supported.");
            }

            _currentTarget.AddSqlProjection(fragment);

            return base.VisitMethodCall(node);
        }

        public class BindingTarget : TargetObject.ISetterBinding
        {
            private readonly string _name;
            private TargetObject.SetterBinding _field;
            private TargetObject.SqlProjectionBinding _sqlProjection;

            public BindingTarget(string name)
            {
                _name = name;
            }

            public void AddMember(MemberInfo memberInfo)
            {
                if (_sqlProjection != null)
                {
                    throw new InvalidOperationException(
                        "Cannot bind to a member after having bound to a sql projection");
                }

                _field ??= new TargetObject.SetterBinding(_name);
                _field.Field.Add(memberInfo);
            }

            public void AddSqlProjection(ISqlFragment sqlProjectionClause)
            {
                if (_field != null)
                {
                    throw new InvalidOperationException(
                        "Cannot bind to a sql projection after having bound to a member.");
                }

                _sqlProjection = new TargetObject.SqlProjectionBinding(_name, sqlProjectionClause);
            }

            public string ToJsonBuildObjectPair(IFieldMapping mapping, ISerializer serializer)
            {
                return _field?.ToJsonBuildObjectPair(mapping, serializer)
                       ?? _sqlProjection?.ToJsonBuildObjectPair(mapping, serializer)
                       ?? string.Empty;
            }
        }

        public class TargetObject
        {
            private readonly IList<ISetterBinding> _setters = new List<ISetterBinding>();

            public TargetObject(Type type)
            {
                Type = type;
            }

            public Type Type { get; }

            public BindingTarget StartBinding(string bindingName)
            {
                var bindingTarget = new BindingTarget(bindingName);
                _setters.Add(bindingTarget);
                return bindingTarget;
            }

            public string ToSelectField(IFieldMapping fields, ISerializer serializer)
            {
                var jsonBuildObjectArgs = _setters.Select(x => x.ToJsonBuildObjectPair(fields, serializer)).Join(", ");
                return $"jsonb_build_object({jsonBuildObjectArgs})";
            }

            public interface ISetterBinding
            {
                string ToJsonBuildObjectPair(IFieldMapping mapping, ISerializer serializer);
            }

            public class SetterBinding: ISetterBinding
            {
                public SetterBinding(string name)
                {
                    Name = name;
                }

                private string Name { get; }
                public SelectedField Field { get; } = new SelectedField();

                public string ToJsonBuildObjectPair(IFieldMapping mapping, ISerializer serializer)
                {
                    var field = mapping.FieldFor(Field.ToArray());
                    var locator = serializer.ValueCasting == ValueCasting.Relaxed
                        ? field.RawLocator ?? field.TypedLocator
                        : field.TypedLocator;

                    return $"'{Name}', {locator}";
                }
            }

            public class SqlProjectionBinding: ISetterBinding
            {
                public SqlProjectionBinding(string name, ISqlFragment projectionFragment)
                {
                    Name = name;
                    ProjectionFragment = projectionFragment;
                }

                private string Name { get; }
                private ISqlFragment ProjectionFragment { get; }

                public string ToJsonBuildObjectPair(IFieldMapping mapping, ISerializer serializer)
                {
                    return $"'{Name}', ({ProjectionFragment.ToSql()})";
                }
            }
        }

        public class SelectedField: IEnumerable<MemberInfo>
        {
            private readonly Stack<MemberInfo> _members = new Stack<MemberInfo>();

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public IEnumerator<MemberInfo> GetEnumerator()
            {
                return _members.GetEnumerator();
            }

            public void Add(MemberInfo member)
            {
                _members.Push(member);
            }
        }
    }
}
