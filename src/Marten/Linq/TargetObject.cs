using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Linq.Fields;
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

        public string ToSelectField(IFieldMapping mapping)
        {
            var jsonBuildObjectArgs = _setters.Select(x => x.ToJsonBuildObjectPair(mapping)).Join(", ");
            return $"jsonb_build_object({jsonBuildObjectArgs})";
        }

        private class SetterBinding
        {
            public SetterBinding(string name)
            {
                Name = name;
            }

            private string Name { get; }
            public SelectedField Field { get; } = new SelectedField();

            public string ToJsonBuildObjectPair(IFieldMapping mapping)
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
