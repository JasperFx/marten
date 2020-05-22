using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Schema;

namespace Marten.Linq
{
    public class TargetObject
    {
        public Type Type { get; }
        private readonly IList<SetterBinding> _setters = new List<SetterBinding>();

        public TargetObject(Type type)
        {
            Type = type;
        }

        public SelectedField StartBinding(string bindingName)
        {
            var setter = new SetterBinding(bindingName);
            _setters.Add(setter);

            return setter.Field;
        }

        public ISelector<T> ToSelector<T>(IQueryableDocument mapping, bool distinct = false)
        {
            return new SelectTransformer<T>(mapping, this, distinct);
        }

        public string ToSelectField(IQueryableDocument mapping)
        {
            var jsonBuildObjectArgs = _setters.Select(x => x.ToJsonBuildObjectPair(mapping)).Join(", ");
            return $"jsonb_build_object({jsonBuildObjectArgs}) as json";
        }

        public ISelector<T> ToJsonSelector<T>(IQueryableDocument mapping)
        {
            var field = ToSelectField(mapping);
            return new JsonSelector(field).As<ISelector<T>>();
        }

        private class SetterBinding
        {
            public SetterBinding(string name)
            {
                Name = name;
            }

            private string Name { get; }
            public SelectedField Field { get; } = new SelectedField();

            public string ToJsonBuildObjectPair(IQueryableDocument mapping)
            {
                var field = mapping.FieldFor(Field.ToArray());
                var locator = field.RawLocator ?? field.TypedLocator;

                return $"'{Name}', {locator}";
            }
        }
    }

    public class SelectedField: IEnumerable<MemberInfo>
    {
        private readonly Stack<MemberInfo> _members = new Stack<MemberInfo>();

        public void Add(MemberInfo member)
        {
            _members.Push(member);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IEnumerator<MemberInfo> GetEnumerator()
        {
            return _members.GetEnumerator();
        }
    }
}
