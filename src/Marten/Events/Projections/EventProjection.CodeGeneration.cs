using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using JasperFx.Events.CodeGeneration;
using Marten.Events.CodeGeneration;
using Marten.Exceptions;
using Marten.Schema;

namespace Marten.Events.Projections;

public abstract partial class EventProjection
{
    protected override void assembleTypes(GeneratedAssembly assembly, StoreOptions options)
    {
        assembly.Rules.Assemblies.Add(GetType().Assembly);
        assembly.Rules.Assemblies.AddRange(_projectMethods.ReferencedAssemblies());
        assembly.Rules.Assemblies.AddRange(_createMethods.ReferencedAssemblies());

        assembly.UsingNamespaces.Add("System.Linq");

        _isAsync = _createMethods.IsAsync || _projectMethods.IsAsync;

        var baseType = _isAsync ? typeof(AsyncEventProjection<>) : typeof(SyncEventProjection<>);
        baseType = baseType.MakeGenericType(GetType());
        _inlineType = assembly.AddType(_inlineTypeName, baseType);

        var method = _inlineType.MethodFor("ApplyEvent");
        method.DerivedVariables.Add(new Variable(GetType(), "Projection"));

        var eventHandling = MethodCollection.AddEventHandling(null, null, _createMethods, _projectMethods);
        method.Frames.Add(eventHandling);
    }

    protected override bool tryAttachTypes(Assembly assembly, StoreOptions options)
    {
        _generatedType = assembly.GetExportedTypes().FirstOrDefault(x => x.Name == _inlineTypeName);
        return _generatedType != null;
    }

    public override void AssembleAndAssertValidity()
    {
        if (!_projectMethods.Methods.Any() && !_createMethods.Methods.Any())
        {
            throw new InvalidProjectionException(
                $"EventProjection {GetType().FullNameInCode()} has no valid projection operations. Either use the Lambda registrations, or expose methods named '{ProjectMethodCollection.MethodName}', '{CreateMethodCollection.MethodName}', or '{CreateDocumentMethodCollection.TransformMethodName}'");
        }

        var invalidMethods = MethodCollection.FindInvalidMethods(GetType(), _projectMethods, _createMethods);

        if (invalidMethods.Any())
        {
            throw new InvalidProjectionException(this, invalidMethods);
        }

        IncludedEventTypes.Fill(MethodCollection.AllEventTypes(_createMethods, _projectMethods));

        foreach (var method in _createMethods.Methods)
        {
            var docType = method.ReturnType;
            if (docType.Closes(typeof(Task<>)))
            {
                RegisterPublishedType(docType.GetGenericArguments().Single());
            }
            else
            {
                RegisterPublishedType(docType);
            }
        }
    }

    protected override IProjection buildProjectionObject(DocumentStore store)
    {
        return _generatedProjection.Value;
    }
}


