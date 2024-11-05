#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core.Operations;
using Weasel.Postgresql;

namespace Marten.Linq.SqlGeneration.Filters;

internal class WriteSerializedJsonParameterFrame: SyncFrame
{
    private readonly string _parametersVariableName;
    private readonly int _parameterIndex;
    private readonly IDictionaryPart _declaration;
    private Variable _builder;

    public WriteSerializedJsonParameterFrame(string parametersVariableName, int parameterIndex,
        IDictionaryPart declaration)
    {
        _parametersVariableName = parametersVariableName;
        _parameterIndex = parameterIndex;
        _declaration = declaration;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.WriteLine($"{_builder.Usage}.{nameof(ICommandBuilder.SetParameterAsJson)}({_parametersVariableName}[{_parameterIndex}], session.Serializer.ToCleanJson({_declaration.Write()}));");

        Next?.GenerateCode(method, writer);
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        // TODO -- use ICommandBuilder instead
        _builder = chain.FindVariable(typeof(IPostgresqlCommandBuilder));
        yield return _builder;
    }
}

/// Tracks the usage of values within a serialized dictionary that is part of either
/// a JSONPath or containment operator query. This is used for compiled query tracking
public class DictionaryValueUsage
{
    public object Value { get; }

    public DictionaryValueUsage(object value)
    {
        Value = value;
    }

    public MemberInfo? QueryMember { get; set; }
}

internal interface IDictionaryPart
{
    string Write();
}


internal record DictionaryValue(string Key, MemberInfo QueryMember) : IDictionaryPart
{
    public string Write()
    {
        return $"{{\"{Key}\", _query.{QueryMember.Name}}}";
    }
}

internal class ArrayContainer: IDictionaryPart
{
    private readonly IDictionaryPart _inner;

    public ArrayContainer(IDictionaryPart inner)
    {
        _inner = inner;
    }

    public string Write()
    {
        return "new object[]{" + _inner.Write() + "}";
    }
}

internal class ArrayDeclaration : IDictionaryPart
{
    public string Key { get; }
    public DictionaryDeclaration Inner { get; }

    public ArrayDeclaration(string key)
    {
        Key = key;
        Inner = new DictionaryDeclaration();
    }

    public string Write()
    {
        return $"{{\"{Key}\", [{Inner.Write()}]}}";
    }
}

internal record ArrayScalarValue(string Key, MemberInfo Member): IDictionaryPart
{
    public string Write()
    {
        return $"{{\"{Key}\", new object[]{{_query.{Member.Name}}}}}";
    }
}


internal class DictionaryDeclaration : IDictionaryPart
{
    public List<IDictionaryPart> Parts { get; } = new();

    public string Write()
    {
        return $"new {typeof(Dictionary<string, object>).FullNameInCode()}{{ {Parts.Select(x => x.Write()).Join(", ")} }}";
    }

    public void ReadDictionary(Dictionary<string,object> data, List<DictionaryValueUsage> usages)
    {
        foreach (var pair in data)
        {
            if (pair.Value is object[] array)
            {
                if (array[0] is Dictionary<string, object> inner)
                {
                    var arrayDeclaration = new ArrayDeclaration(pair.Key);
                    Parts.Add(arrayDeclaration);

                    arrayDeclaration.Inner.ReadDictionary(inner, usages);
                }
                else
                {
                    var usage = usages.FirstOrDefault(x => x.Value.Equals(array[0]));
                    if (usage != null)
                    {
                        var arrayValue = new ArrayScalarValue(pair.Key, usage.QueryMember!);
                        Parts.Add(arrayValue);
                    }
                }


            }
            else if (pair.Value is Dictionary<string, object> dict)
            {
                var child = new DictionaryDeclaration();
                Parts.Add(child);
                child.ReadDictionary(dict, usages);
            }
            else
            {
                var usage = usages.FirstOrDefault(x => x.Value.Equals(pair.Value));
                if (usage != null)
                {
                    var value = new DictionaryValue(pair.Key, usage.QueryMember!);
                    Parts.Add(value);
                }
                else
                {
                    throw new NotImplementedException("Not handling constants here yet:(");
                }
            }
        }
    }
}

