using System;
 using System.Collections;
 using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Parsing;
 using Marten.Linq.QueryHandlers;
 using Marten.Schema;
 using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;
using ReflectionExtensions = LamarCodeGeneration.Util.ReflectionExtensions;

 namespace Marten.Linq.Fields
{
    /// <summary>
    /// Internal representation of queryable members within a Linq query
    /// </summary>
    public interface IFieldMapping
    {
        IField FieldFor(Expression expression);
        IField FieldFor(IEnumerable<MemberInfo> members);
        IField FieldFor(MemberInfo member);
        public IField FieldFor(string memberName);
        PropertySearching PropertySearching { get; }
        DeleteStyle DeleteStyle { get; }
    }

    public class FieldMapping: IFieldMapping
    {
        private readonly string _dataLocator;
        private readonly Type _documentType;
        private readonly StoreOptions _options;
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private readonly ISerializer _serializer;

        internal FieldMapping(string dataLocator, Type documentType, StoreOptions options)
        {
            _dataLocator = dataLocator;
            _documentType = documentType;
            _options = options;
            _serializer = options.Serializer();
        }

        public PropertySearching PropertySearching { get; set; } = PropertySearching.JSON_Locator_Only;
        public DeleteStyle DeleteStyle { get; set; } = DeleteStyle.Remove;


        protected void removeIdField()
        {
            var idFields = _fields.Where(x => x.Value is IdField).ToArray();
            foreach (var pair in idFields)
            {
                _fields.TryRemove(pair.Key, out _);
            }
        }

        protected void setField(string name, IField field)
        {
            _fields[name] = field;
        }

        protected IEnumerable<IField> fields()
        {
            return _fields.Values;
        }

        public IField FieldFor(Expression expression)
        {
            return FieldFor(FindMembers.Determine(expression));
        }



        public IField FieldFor(IEnumerable<MemberInfo> members)
        {
            if (members.Count() == 1)
            {
                return FieldFor(members.Single());
            }

            var key = members.Select(x => x.Name).Join("");

            return _fields.GetOrAdd(key,
                _ => resolveField(members.ToArray()));
        }


        public IField FieldFor(MemberInfo member)
        {
            return _fields.GetOrAdd(member.Name,
                _ => resolveField(new []{member}));
        }

        protected IField resolveField(MemberInfo[] members)
        {
            if (!members.Any())
            {
                if (_documentType.IsSimple())
                {
                    return new SimpleDataField(_documentType);
                }

                throw new ArgumentOutOfRangeException(nameof(members),"No members found in this Expression");
            }

            foreach (var source in _options.Linq.FieldSources)
            {
                if (source.TryResolve(_dataLocator, _options, _serializer, _documentType, members, out var field))
                {
                    return field;
                }
            }

            // List.Count
            if (members.Length > 1 && members.Last().Name == "Count")
            {
                if (ReflectionExtensions.IsEnumerable(members[members.Length - 2].GetMemberType()))
                {
                    var inner = (ArrayField)FieldFor(members.Take(members.Length - 1).ToArray());
                    return new CollectionLengthField(inner, members);
                }
            }

            // Array.Length
            if (members.Last().Equals(LinqConstants.ArrayLength))
            {
                var inner = (ArrayField)FieldFor(members.Take(members.Length - 1).ToArray());
                return new CollectionLengthField(inner, members);
            }

            var fieldType = members.Last().GetRawMemberType();



            if (fieldType.IsNullable())
            {
                var innerFieldType = fieldType.GetGenericArguments()[0];
                var innerField = createFieldByFieldType(members, innerFieldType);

                return new NullableTypeField(innerField);
            }




            return createFieldByFieldType(members, fieldType);
        }

        private IField createFieldByFieldType(MemberInfo[] members, Type fieldType)
        {
            if (fieldType == typeof(string))
            {
                return new StringField(_dataLocator, _serializer.Casing, members);
            }

            if (fieldType.Closes(typeof(IDictionary<,>)))
            {
                return new DictionaryField(_dataLocator, _serializer.Casing, _serializer.EnumStorage, members);
            }

            if (fieldType.IsEnum)
            {
                return _serializer.EnumStorage == EnumStorage.AsInteger
                    ? (IField) new EnumAsIntegerField(_dataLocator, _serializer.Casing, members)
                    : new EnumAsStringField(_dataLocator, _serializer.Casing, members);
            }

            if (fieldType == typeof(DateTime))
            {
                return new DateTimeField(_dataLocator, _options.DatabaseSchemaName, _serializer.Casing, members);
            }

            if (fieldType == typeof(DateTimeOffset))
            {
                return new DateTimeOffsetField(_dataLocator, _options.DatabaseSchemaName, _serializer.Casing, members);
            }


            var pgType = PostgresqlProvider.Instance.GetDatabaseType(fieldType, _serializer.EnumStorage);


            if (fieldType.Closes(typeof(IDictionary<,>)))
            {
                return new SimpleCastField(_dataLocator, "JSONB", _serializer.Casing, members);
            }

            if (isEnumerable(fieldType))
            {
                return new ArrayField(_dataLocator, pgType, _serializer, members);
            }

            if (pgType.IsNotEmpty())
            {
                return new SimpleCastField(_dataLocator, pgType, _serializer.Casing, members);
            }

            throw new NotSupportedException(
                $"Marten does not support Linq expressions for this member. Was {_documentType.FullName}.{members.Select(x => x.Name).Join(".")}");
        }

        private static bool isEnumerable(Type fieldType)
        {
            return fieldType.IsArray || fieldType.Closes(typeof(IEnumerable<>));
        }

        public IField FieldFor(string memberName)
        {
            return _fields.GetOrAdd(memberName, name =>
            {
                var member = _documentType.GetProperties().FirstOrDefault(x => x.Name == name).As<MemberInfo>() ??
                             _documentType.GetFields().FirstOrDefault(x => x.Name == name);

                if (member == null) return null;




                return resolveField(new MemberInfo[] {member});
            });
        }

    }


}
