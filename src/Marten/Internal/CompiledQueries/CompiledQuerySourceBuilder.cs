using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.Storage;
using Marten.Linq.Includes;
using Marten.Linq.QueryHandlers;
using Marten.Schema.Arguments;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries;

internal class CompiledQuerySourceBuilder
{
    private readonly DocumentTracking _documentTracking;
    private readonly CompiledQueryPlan _plan;
    private readonly StoreOptions _storeOptions;
    private readonly string _typeName;

    public CompiledQuerySourceBuilder(CompiledQueryPlan plan, StoreOptions storeOptions,
        DocumentTracking documentTracking)
    {
        _plan = plan;
        _storeOptions = storeOptions;
        _documentTracking = documentTracking;
        _typeName = documentTracking + plan.QueryType.ToSuffixedTypeName("CompiledQuerySource");
    }

    public void AssembleTypes(GeneratedAssembly assembly)
    {
        assembly.UsingNamespaces.Fill("System");

        foreach (var referencedAssembly in WalkReferencedAssemblies.ForTypes(
                     typeof(IDocumentStorage<>),
                     _plan.QueryType,
                     _plan.OutputType))
            assembly.Rules.Assemblies.Fill(referencedAssembly);

        var handlerType = determineHandlerType();

        var hardcoded = new HardCodedParameters(_plan);
        var compiledHandlerType = buildHandlerType(assembly, handlerType, hardcoded);

        buildSourceType(assembly, handlerType, compiledHandlerType);
    }

    public ICompiledQuerySource Build(Type sourceType)
    {
        var hardcoded = new HardCodedParameters(_plan);

        return (ICompiledQuerySource)Activator.CreateInstance(sourceType, hardcoded, _plan.HandlerPrototype);
    }

    private void buildSourceType(GeneratedAssembly assembly, CompiledSourceType handlerType,
        GeneratedType compiledHandlerType)
    {
        var sourceBaseType = typeof(CompiledQuerySource<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);
        var sourceType = assembly.AddType(_typeName, sourceBaseType);

        var hardcoded = new InjectedField(typeof(HardCodedParameters), "hardcoded");
        sourceType.AllInjectedFields.Add(hardcoded);

        var buildHandler = sourceType.MethodFor("BuildHandler");
        switch (handlerType)
        {
            case CompiledSourceType.Cloneable:
                var innerField = new InjectedField(typeof(IMaybeStatefulHandler));
                sourceType.AllInjectedFields.Add(innerField);

                var statistics = _plan.StatisticsMember == null ? "null" : $"query.{_plan.StatisticsMember.Name}";
                buildHandler.Frames.Code(
                    $"return new {assembly.Namespace}.{compiledHandlerType.TypeName}({innerField.Usage}, query, {statistics}, _hardcoded);");
                break;

            case CompiledSourceType.Stateless:
                var inner = new InjectedField(typeof(IQueryHandler<>).MakeGenericType(_plan.OutputType));
                sourceType.AllInjectedFields.Add(inner);

                buildHandler.Frames.Code(
                    $"return new {assembly.Namespace}.{compiledHandlerType.TypeName}({inner.Usage}, query, _hardcoded);");
                break;

            case CompiledSourceType.Complex:
                var innerField2 = new InjectedField(typeof(IMaybeStatefulHandler));
                sourceType.AllInjectedFields.Add(innerField2);

                buildHandler.Frames.Code(
                    $"return new {assembly.Namespace}.{compiledHandlerType.TypeName}({innerField2.Usage}, query, _hardcoded);");
                break;
        }
    }

    private GeneratedType buildHandlerType(GeneratedAssembly assembly,
        CompiledSourceType handlerType, HardCodedParameters hardcoded)
    {
        var queryTypeName = _documentTracking + _plan.QueryType.ToSuffixedTypeName("CompiledQuery");
        var baseType = determineBaseType(handlerType);
        var type = assembly.AddType(queryTypeName, baseType);


        configureCommandMethod(type, hardcoded);

        if (handlerType == CompiledSourceType.Complex)
        {
            buildHandlerMethod(type);
        }

        return type;
    }

    private void buildHandlerMethod(GeneratedType compiledType)
    {
        var method = compiledType.MethodFor("BuildHandler");

        var handlerName = "_inner";

        // first build out the inner
        if (_plan.HandlerPrototype is IMaybeStatefulHandler h && h.DependsOnDocumentSelector())
        {
            handlerName = "cloned";

            var statistics = _plan.StatisticsMember == null ? "null" : $"query.{_plan.StatisticsMember.Name}";

            method.Frames.Code(
                $"var cloned = _inner.{nameof(IMaybeStatefulHandler.CloneForSession)}(session, {statistics});");
        }

        if (_plan.IncludeMembers.Any())
        {
            var readers = _plan.IncludeMembers.Select(buildIncludeReader);

            var includeHandlerType = typeof(IncludeQueryHandler<>).MakeGenericType(_plan.OutputType);

            var readerArray = "{{" + readers.Join(", ") + "}}";

            var constructorHandlerType = typeof(IQueryHandler<>).MakeGenericType(_plan.OutputType);

            method.Frames.Code(
                $"var includeWriters = new {typeof(IIncludeReader).FullNameInCode()}[]{readerArray};");
            method.Frames.Code(
                $"var included = new {includeHandlerType.FullNameInCode()}(({constructorHandlerType.FullNameInCode()}){handlerName}, includeWriters);");

            handlerName = "included";
        }

        method.Frames.Code($"return {handlerName};");
    }

    private string buildIncludeReader(MemberInfo member)
    {
        var memberType = member.GetMemberType();
        if (memberType.Closes(typeof(Action<>)))
        {
            return
                $"{typeof(Include).FullNameInCode()}.{nameof(Include.ReaderToAction)}(session, _query.{member.Name})";
        }

        if (memberType.Closes(typeof(IList<>)))
        {
            return
                $"{typeof(Include).FullNameInCode()}.{nameof(Include.ReaderToList)}(session, _query.{member.Name})";
        }

        if (memberType.Closes(typeof(IDictionary<,>)))
        {
            var includeType = memberType.GetGenericArguments().Last();
            var idType = memberType.GetGenericArguments().First();
            return
                $"{typeof(Include).FullNameInCode()}.{nameof(Include.ReaderToDictionary)}<{includeType.FullNameInCode()},{idType.FullNameInCode()}>(session, _query.{member.Name})";
        }

        throw new ArgumentOutOfRangeException();
    }


    private void configureCommandMethod(GeneratedType compiledType, HardCodedParameters hardcoded)
    {
        var method = compiledType.MethodFor(nameof(IQueryHandler.ConfigureCommand));
        var correctedCommandText = _plan.CorrectedCommandText();

        method.Frames.Code($"var parameters = {{0}}.{nameof(CommandBuilder.AppendWithParameters)}(@{{1}}, '{CompiledQueryPlan.ParameterPlaceholder}');",
            Use.Type<CommandBuilder>(), correctedCommandText);

        foreach (var parameter in _plan.Parameters) parameter.GenerateCode(method, _storeOptions);

        if (hardcoded.HasTenantId)
        {
            method.Frames.Code($"{{0}}.{nameof(CommandBuilder.AddNamedParameter)}({{1}}, session.TenantId);",
                Use.Type<CommandBuilder>(), TenantIdArgument.ArgName);
        }

        if (hardcoded.HasAny())
        {
            method.Frames.Code($"_hardcoded.{nameof(HardCodedParameters.Apply)}(parameters);");
        }
    }

    private Type determineBaseType(CompiledSourceType sourceType)
    {
        switch (sourceType)
        {
            case CompiledSourceType.Cloneable:
                return typeof(ClonedCompiledQuery<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);

            case CompiledSourceType.Complex:
                return typeof(ComplexCompiledQuery<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);

            case CompiledSourceType.Stateless:
                return typeof(StatelessCompiledQuery<,>).MakeGenericType(_plan.OutputType, _plan.QueryType);
        }

        throw new ArgumentOutOfRangeException();
    }

    private CompiledSourceType determineHandlerType()
    {
        if (_plan.IncludeMembers.Any())
        {
            return CompiledSourceType.Complex;
        }

        if (_plan.HandlerPrototype is IMaybeStatefulHandler h)
        {
            if (h.DependsOnDocumentSelector())
            {
                return CompiledSourceType.Cloneable;
            }
        }

        return CompiledSourceType.Stateless;
    }

}
