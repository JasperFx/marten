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
    public class DocumentFunctionOperationBuilder
    {
        private readonly DocumentMapping _mapping;
        private readonly UpsertFunction _function;
        private readonly StorageRole _role;

        public DocumentFunctionOperationBuilder(DocumentMapping mapping, UpsertFunction function, StorageRole role)
        {
            _function = function;
            _role = role;

            CommandText = $"select {_function.Identifier}({_function.Arguments.Select(x => "?").Join(", ")})";

            ClassName = $"{function.GetType().Name.Replace("Function", "")}{ReflectionExtensions.NameInCode(mapping.DocumentType)}Operation";

            _mapping = mapping;

        }

        public string ClassName { get; }
        public string CommandText { get; }

        public GeneratedType BuildType(GeneratedAssembly assembly)
        {
            var baseType = typeof(StorageOperation<,>).MakeGenericType(_mapping.DocumentType, _mapping.IdType);
            var type = assembly.AddType(ClassName, baseType);

            type.MethodFor("Role").Frames.Return(Constant.ForEnum(_role));
            type.MethodFor("DbType").Frames.Return(Constant.ForEnum(TypeMappings.ToDbType(_mapping.IdType)));
            type.MethodFor("CommandText").Frames.Return(Constant.ForString(CommandText));

            buildConfigureMethod(type);

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

        private void buildConfigureMethod(GeneratedType type)
        {
            var method = type.MethodFor("ConfigureParameters");
            var parameters = method.Arguments[0];

            for (int i = 2; i < _function.Arguments.Count; i++)
            {
                var argument = _function.Arguments[i];
                var frame = buildArgumentFrame(argument, i, parameters, type);
                method.Frames.Add(frame);
            }


        }

        private Frame buildArgumentFrame(UpsertArgument argument, int position, Variable parameters, GeneratedType type)
        {
            switch (argument.Arg)
            {

                case "docVersion":
                    var version = type.AllInjectedFields[2];
                    return new SetParameterFrame(parameters, version, position, NpgsqlDbType.Uuid);

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
