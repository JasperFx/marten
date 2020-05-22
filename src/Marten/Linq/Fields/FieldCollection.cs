﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Util;

namespace Marten.Linq.Fields
{
    public class FieldCollection
    {
        private readonly string _dataLocator;
        private readonly Type _documentType;
        private readonly StoreOptions _options;
        private readonly ConcurrentDictionary<string, IField> _fields = new ConcurrentDictionary<string, IField>();
        private readonly ISerializer _serializer;

        public FieldCollection(string dataLocator, Type documentType, StoreOptions options)
        {
            _dataLocator = dataLocator;
            _documentType = documentType;
            _options = options;
            _serializer = options.Serializer();
        }


        protected void removeIdField()
        {
            var idFields = _fields.Where(x => x.Value is IdField).ToArray();
            foreach (var pair in idFields)
            {
                IField field;
                _fields.TryRemove(pair.Key, out field);
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
                name => resolveField(new []{member}));
        }

        protected IField resolveField(MemberInfo[] members)
        {
            foreach (var source in _options.FieldSources)
            {
                if (source.TryResolve(_dataLocator, _options, _serializer, _documentType, members, out var field))
                {
                    return field;
                }
            }

            var fieldType = members.Last().GetMemberType();

            if (fieldType == typeof(string))
            {
                return new StringField(_dataLocator, _serializer.Casing, members);
            }



            if (fieldType.IsEnum)
            {
                return _serializer.EnumStorage == EnumStorage.AsInteger
                    ? (IField) new EnumAsIntegerField(_dataLocator, _serializer.Casing, members)
                    : new EnumAsStringField(_dataLocator, _serializer.Casing, members);
            }

            if (fieldType == typeof(DateTime) || fieldType == typeof(DateTime?))
            {
                return new DateTimeField(_dataLocator, _options.DatabaseSchemaName, _serializer.Casing, members);
            }

            if (fieldType == typeof(DateTimeOffset) || fieldType == typeof(DateTimeOffset?))
            {
                return new DateTimeOffsetField(_dataLocator, _options.DatabaseSchemaName, _serializer.Casing, members);
            }



            var pgType = TypeMappings.GetPgType(fieldType, _serializer.EnumStorage);


            if (pgType.IsNotEmpty())
            {
                if (fieldType.IsArray || fieldType.Closes(typeof(IList<>)))
                {
                    return new ArrayField(_dataLocator, pgType, _serializer.Casing, members);
                }

                return new SimpleCastField(_dataLocator, pgType, _serializer.Casing, members);
            }

            throw new NotSupportedException($"Marten does not support Linq expressions for this member. Was {_documentType.FullName}.{members.Select(x => x.Name).Join(".")}");


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
