using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Util;

namespace Marten.Linq.Parsing;

internal class SelectTransformBuilder: ExpressionVisitor
{
    private SelectedField _currentField;
    private TargetObject _target;

    public SelectTransformBuilder(Expression clause, IQueryableMemberCollection members, ISerializer serializer)
    {
        // ReSharper disable once VirtualMemberCallInConstructor
        Visit(clause);
        SelectedFieldExpression = _target.ToSelectField(members, serializer);
    }

    public string SelectedFieldExpression { get; }

    protected override Expression VisitNew(NewExpression expression)
    {
        if (_target != null)
        {
            return base.VisitNew(expression);
        }

        _target = new TargetObject(expression.Type);

        var parameters = expression.Constructor.GetParameters();

        for (var i = 0; i < parameters.Length; i++)
        {
            _currentField = _target.StartBinding(parameters[i].Name);
            Visit(expression.Arguments[i]);
        }

        return expression;
    }

    protected override Expression VisitMember(MemberExpression node)
    {
        _currentField.Add(node.Member);
        return base.VisitMember(node);
    }

    protected override MemberBinding VisitMemberBinding(MemberBinding node)
    {
        _currentField = _target.StartBinding(node.Member.Name);

        return base.VisitMemberBinding(node);
    }


    public class TargetObject
    {
        private readonly IList<SetterBinding> _setters = new List<SetterBinding>();

        public TargetObject(Type type)
        {
            Type = type;
        }

        public Type Type { get; }

        public SelectedField StartBinding(string bindingName)
        {
            var setter = new SetterBinding(bindingName);
            _setters.Add(setter);

            return setter.Field;
        }

        public string ToSelectField(IQueryableMemberCollection fields, ISerializer serializer)
        {
            var jsonBuildObjectArgs = _setters.Select(x => x.ToJsonBuildObjectPair(fields, serializer)).Join(", ");
            return $"jsonb_build_object({jsonBuildObjectArgs})";
        }

        private class SetterBinding
        {
            public SetterBinding(string name)
            {
                Name = name;
            }

            private string Name { get; }
            public SelectedField Field { get; } = new();

            public string ToJsonBuildObjectPair(IQueryableMemberCollection mapping, ISerializer serializer)
            {
                var field = mapping.MemberFor(Field.ToArray());
                var locator = serializer.ValueCasting == ValueCasting.Relaxed
                    ? field.RawLocator ?? field.TypedLocator
                    : field.TypedLocator;

                if (field is IDictionaryMember)
                {
                    // DictionaryField.RawLocator does not have cast to JSONB so TypedLocator is used
                    locator = field.TypedLocator;
                }

                if (field.MemberType.IsClass && field.MemberType != typeof(string) &&
                    field.MemberType != typeof(decimal))
                {
                    // If the field is a class, we need to cast it to JSONB otherwise it will be serialized to plain string and fail to deserialize later on
                    locator = field.JSONBLocator;
                }

                return $"'{Name.FormatCase(serializer.Casing)}', {locator}";
            }
        }
    }

    public class SelectedField: IEnumerable<MemberInfo>
    {
        private readonly Stack<MemberInfo> _members = new();

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
