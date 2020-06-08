using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using ReflectionExtensions = LamarCodeGeneration.ReflectionExtensions;

namespace Marten.V4Internals
{
    public class UpsertOperationBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly Setter _commandText;
        private readonly UpsertFunction _function;
        private InjectedField _documentField;
        private InjectedField _versionField;
        public const string CommandTextConstantName = "CommandText";

        public UpsertOperationBuilder(DocumentMapping mapping)
        {
            _function = new UpsertFunction(mapping);

            CommandText = $"select {_function.Identifier}({_function.Arguments.Select(x => "?").Join(", ")})";

            ClassName = $"Upsert{ReflectionExtensions.NameInCode(mapping.DocumentType)}Operation";

            _commandText = Setter.Constant(CommandTextConstantName, Constant.ForString(CommandText));
            _mapping = mapping;

            _documentField = new InjectedField(_mapping.DocumentType, "document");
            _versionField = new InjectedField(typeof(Guid), "version");
        }

        public string ClassName { get; }
        public string CommandText { get; }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var type = assembly.AddType(ClassName, typeof(object));
            type.Implements<IStorageOperation>();

            // TODO -- this is a LamarCodeGeneration bug. Needs to walk up the hierarchy
            type.Implements<IQueryHandler>();

            type.AllInjectedFields.Add(_documentField);
            type.AllInjectedFields.Add(_versionField);

            type.Setters.Add(_commandText);
            type.Setters.Add(Setter.ReadOnly(nameof(IStorageOperation.Role), Constant.ForEnum(StorageRole.Upsert)));
            type.Setters.Add(Setter.ReadOnly(nameof(IStorageOperation.DocumentType), Constant.ForType(_mapping.DocumentType)));


            buildConfigureMethod(type.MethodFor(nameof(IStorageOperation.ConfigureCommand)));

            if (_mapping.UseOptimisticConcurrency)
            {
                throw new NotImplementedException("Not ready for this yet!");
            }
            else
            {
                type.MethodFor(nameof(IStorageOperation.Postprocess)).Frames.Add(new CommentFrame("Nothing"));
                var postprocessAsync = type.MethodFor(nameof(IStorageOperation.PostprocessAsync));
                postprocessAsync.AsyncMode = AsyncMode.ReturnCompletedTask;
                postprocessAsync.Frames.Add(new CommentFrame("Nothing"));
            }

            return type;
        }

        private void buildConfigureMethod(GeneratedMethod method)
        {
            method.Frames.Add(new CommentFrame("Nothing yet..."));

            var append = MethodCall.For<CommandBuilder>(x => x.AppendWithParameters(null));
            append.Arguments[0] = _commandText;

            var parameters = append.ReturnVariable;

            for (int i = 0; i < _function.Arguments.Count; i++)
            {
                var argument = _function.Arguments[i];
                var frame = buildArgumentFrame(argument, i, parameters);
                method.Frames.Add(frame);
            }


        }

        private Frame buildArgumentFrame(UpsertArgument argument, int position, Variable parameters)
        {
            switch (argument.Arg)
            {
                case "docId":
                    var id = new Variable(_mapping.IdType, $"{_documentField.Usage}.{_mapping.IdMember.Name}");
                    return new SetParameterFrame(parameters, id, position, TypeMappings.ToDbType(_mapping.IdType));

                case "doc":
                    return new WriteJsonBFrame(parameters, _documentField, position);

                case "docVersion":
                    return new SetParameterFrame(parameters, _versionField, position, NpgsqlDbType.Uuid);

                case "docDotNetType":
                    var typeName = new Variable(typeof(string), $"_document.GetType().FullName");
                    return new SetParameterFrame(parameters, typeName, position, NpgsqlDbType.Varchar);
            }

            throw new NotSupportedException($"Marten does not yet know how to generate code for {argument} with name {argument.Arg}");
        }
    }

    public class WriteJsonBFrame: SyncFrame
    {
        private readonly Variable _parameters;
        private readonly Variable _doc;
        private readonly int _position;
        private Variable _session;

        public WriteJsonBFrame(Variable parameters, Variable doc, int position)
        {
            _parameters = parameters;
            _doc = doc;
            _position = position;

            uses.Add(parameters);
            uses.Add(doc);
        }

        public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
        {
            _session = chain.FindVariable(typeof(IMartenSession));
            yield return _session;
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"{_parameters.Usage}[{_position}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {Constant.ForEnum(NpgsqlDbType.Jsonb).Usage};");
            writer.WriteLine($"{_parameters.Usage}[{_position}].{nameof(NpgsqlParameter.Value)} = {_session.Usage}.{nameof(IMartenSession.Serializer)}.{nameof(ISerializer.ToJson)}({_doc.Usage});");
            writer.BlankLine();

            Next?.GenerateCode(method, writer);
        }
    }


    public class SetParameterFrame: SyncFrame
    {
        private readonly Variable _parameters;
        private readonly Variable _value;
        private readonly int _position;
        private readonly NpgsqlDbType _dbType;

        public SetParameterFrame(Variable parameters, Variable value, int position, NpgsqlDbType dbType)
        {
            _parameters = parameters;
            _value = value;
            _position = position;
            _dbType = dbType;
            uses.Add(parameters);
            uses.Add(value);
        }

        public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
        {
            writer.WriteLine($"{_parameters.Usage}[{_position}].{nameof(NpgsqlParameter.NpgsqlDbType)} = {Constant.ForEnum(_dbType).Usage};");
            writer.WriteLine($"{_parameters.Usage}[{_position}].{nameof(NpgsqlParameter.Value)} = {_value.Usage};");
            writer.BlankLine();

            Next?.GenerateCode(method, writer);
        }
    }
}
