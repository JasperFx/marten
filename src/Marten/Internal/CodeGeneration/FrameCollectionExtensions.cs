using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Baseline;
using Baseline.Expressions;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Util;
using Weasel.Postgresql;
using FindMembers = Marten.Linq.Parsing.FindMembers;

namespace Marten.Internal.CodeGeneration
{
    internal static class FrameCollectionExtensions
    {
        public const string DocumentVariableName = "document";

        public static void StoreInIdentityMap(this FramesCollection frames, DocumentMapping mapping)
        {
            frames.Code("_identityMap[id] = document;");
        }

        public static void StoreTracker(this FramesCollection frames)
        {
            frames.Code("StoreTracker({0}, document);", Use.Type<IMartenSession>());
        }

        public static void DeserializeDocument(this FramesCollection frames, DocumentMapping mapping, int index)
        {
            var documentType = mapping.DocumentType;
            var document = new Variable(documentType, DocumentVariableName);
            if (mapping is DocumentMapping d)
            {
                if (!d.IsHierarchy())
                {
                    frames.Code($@"
{documentType.FullNameInCode()} document;
document = _serializer.FromJson<{documentType.FullNameInCode()}>(reader, {index});
").Creates(document);
                }
                else
                {
                    // Hierarchy path is different
                    frames.Code($@"
{documentType.FullNameInCode()} document;
var typeAlias = reader.GetFieldValue<string>({index + 1});
document = ({documentType.FullNameInCode()}) _serializer.FromJson(_mapping.TypeFor(typeAlias), reader, {index});
").Creates(document);
                }
            }
        }

        public static void MarkAsLoaded(this FramesCollection frames)
        {
            frames.Code($"{{0}}.{nameof(IMartenSession.MarkAsDocumentLoaded)}(id, document);", Use.Type<IMartenSession>());
        }

        public static void DeserializeDocumentAsync(this FramesCollection frames, DocumentMapping mapping, int index)
        {
            var documentType = mapping.DocumentType;
            var document = new Variable(documentType, DocumentVariableName);

            if (!mapping.IsHierarchy())
            {
                frames.Code($@"
{documentType.FullNameInCode()} document;
document = _serializer.FromJson<{documentType.FullNameInCode()}>(reader, {index});
").Creates(document);
            }
            else
            {
                frames.CodeAsync($@"
{documentType.FullNameInCode()} document;
var typeAlias = await reader.GetFieldValueAsync<string>({index + 1}, {{0}});
document = ({documentType.FullNameInCode()}) (await _serializer.FromJsonAsync(_mapping.TypeFor(typeAlias), reader, {index}, {{0}}));
", Use.Type<CancellationToken>()).Creates(document);
            }



        }

        /// <summary>
        /// Generates the necessary setter code to set a value of a document.
        /// Handles internal/private setters
        /// </summary>
        /// <param name="frames"></param>
        /// <param name="member"></param>
        /// <param name="variableName"></param>
        /// <param name="documentType"></param>
        /// <param name="generatedType"></param>
        public static void SetMemberValue(this FramesCollection frames, MemberInfo member, string variableName, Type documentType, GeneratedType generatedType)
        {
            if (member is PropertyInfo property)
            {
                if (property.CanWrite)
                {
                    if (property.SetMethod.IsPublic)
                    {
                        frames.SetPublicMemberValue(member, variableName, documentType);
                    }
                    else
                    {
                        var setterFieldName = generatedType.InitializeLambdaSetterProperty(member, documentType);
                        frames.Code($"{setterFieldName}({{0}}, {variableName});", new Use(documentType));
                    }

                    return;
                }
            }
            else if (member is FieldInfo field)
            {
                if (field.IsPublic)
                {
                    frames.SetPublicMemberValue(member, variableName, documentType);
                }
                else
                {
                    var setterFieldName = generatedType.InitializeLambdaSetterProperty(member, documentType);
                    frames.Code($"{setterFieldName}({{0}}, {variableName});", new Use(documentType));
                }

                return;
            }

            throw new ArgumentOutOfRangeException(nameof(member), $"MemberInfo {member} is not valid in this usage. ");
        }

        public static string InitializeLambdaSetterProperty(this GeneratedType generatedType, MemberInfo member, Type documentType)
        {
            var setterFieldName = $"{member.Name}Writer";

            if (generatedType.Setters.All(x => x.PropName != setterFieldName))
            {
                var memberType = member.GetRawMemberType();
                var actionType = typeof(Action<,>).MakeGenericType(documentType, memberType);
                var expression = $"{typeof(LambdaBuilder).GetFullName()}.{nameof(LambdaBuilder.Setter)}<{documentType.FullNameInCode()},{memberType.FullNameInCode()}>(typeof({documentType.FullNameInCode()}).GetProperty(\"{member.Name}\"))";

                var constant = new Variable(actionType, expression);

                var setter = Setter.StaticReadOnly(setterFieldName, constant);

                generatedType.Setters.Add(setter);

            }

            return setterFieldName;
        }

        private static void SetPublicMemberValue(this FramesCollection frames, MemberInfo member, string variableName,
            Type documentType)
        {
            frames.Code($"{{0}}.{member.Name} = {variableName};", new Use(documentType));
        }

        private interface ISetterBuilder
        {
            void Add(GeneratedType generatedType, MemberInfo member, string setterFieldName);
        }

        private class SetterBuilder<TTarget, TMember>: ISetterBuilder
        {
            public void Add(GeneratedType generatedType, MemberInfo member, string setterFieldName)
            {
                var writer = LambdaBuilder.Setter<TTarget, TMember>(member);
                var setter =
                    new Setter(typeof(Action<TTarget, TMember>), setterFieldName)
                    {
                        InitialValue = writer, Type = SetterType.ReadWrite
                    };

                generatedType.Setters.Add(setter);

            }
        }

        public static void AssignMemberFromReader<T>(this GeneratedMethod method, GeneratedType generatedType, int index,
            Expression<Func<T, object>> memberExpression)
        {
            var member = FindMembers.Determine(memberExpression).Single();
            var variableName = member.Name.ToCamelCase();
            method.Frames.Code($"var {variableName} = reader.GetFieldValue<{member.GetMemberType().FullNameInCode()}>({index});");

            method.Frames.SetMemberValue(member, variableName, typeof(T), generatedType);
        }

        public static void AssignMemberFromReader(this GeneratedMethod method, GeneratedType generatedType, int index,
            Type documentType, string memberName)
        {
            var member = documentType.GetMember(memberName).Single();
            var variableName = member.Name.ToCamelCase();
            method.Frames.Code($"var {variableName} = reader.GetFieldValue<{member.GetMemberType().FullNameInCode()}>({index});");

            method.Frames.SetMemberValue(member, variableName, documentType, generatedType);
        }

        public static void AssignMemberFromReaderAsync<T>(this GeneratedMethod method, GeneratedType generatedType, int index,
            Expression<Func<T, object>> memberExpression)
        {
            var member = FindMembers.Determine(memberExpression).Single();
            var variableName = member.Name.ToCamelCase();
            method.Frames.Code($"var {variableName} = await reader.GetFieldValueAsync<{member.GetMemberType().FullNameInCode()}>({index}, {{0}});", Use.Type<CancellationToken>());

            method.Frames.SetMemberValue(member, variableName, typeof(T), generatedType);
        }

        public static void AssignMemberFromReaderAsync(this GeneratedMethod method, GeneratedType generatedType, int index,
            Type documentType, string memberName)
        {
            var member = documentType.GetMember(memberName).Single();
            var variableName = member.Name.ToCamelCase();
            method.Frames.Code($"var {variableName} = await reader.GetFieldValueAsync<{member.GetMemberType().FullNameInCode()}>({index}, {{0}});", Use.Type<CancellationToken>());

            method.Frames.SetMemberValue(member, variableName, documentType, generatedType);
        }

        public static void IfDbReaderValueIsNotNull(this GeneratedMethod method, int index, Action action)
        {
            method.Frames.Code($"if (!reader.IsDBNull({index}))");
            method.Frames.Code("{{");

            action();

            method.Frames.Code("}}");
        }

        public static void IfDbReaderValueIsNotNullAsync(this GeneratedMethod method, int index, Action action)
        {
            method.Frames.CodeAsync($"if (!(await reader.IsDBNullAsync({index}, token)))");
            method.Frames.Code("{{");

            action();

            method.Frames.Code("}}");
        }

        public static void SetParameterFromMember<T>(this GeneratedMethod method, int index,
            Expression<Func<T, object>> memberExpression)
        {
            var member = FindMembers.Determine(memberExpression).Single();
            var memberType = member.GetMemberType();
            var pgType = PostgresqlProvider.Instance.ToParameterType(memberType);

            if (memberType == typeof(string))
            {
                method.Frames.Code($"parameters[{index}].Value = {{0}}.{member.Name} != null ? (object){{0}}.{member.Name} : {typeof(DBNull).FullNameInCode()}.Value;", Use.Type<T>());
                method.Frames.Code($"parameters[{index}].NpgsqlDbType = {{0}};", pgType);
            }
            else
            {
                method.Frames.Code($"parameters[{index}].Value = {{0}}.{member.Name};", Use.Type<T>());
                method.Frames.Code($"parameters[{index}].NpgsqlDbType = {{0}};", pgType);
            }
        }
    }
}
