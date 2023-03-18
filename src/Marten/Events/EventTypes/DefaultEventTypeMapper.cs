using System;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten.Exceptions;
using Marten.Util;

namespace Marten.Events.EventTypes;

public interface IEventTypeMapper
{
    /// <summary>
    ///     Translates by convention the CLR type name into string event type name.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <param name="eventType">CLR event type</param>
    /// <returns>Mapped string event type name</returns>
    string GetEventTypeName(Type eventType);

    /// <summary>
    ///     Translates by convention the CLR type name into string event type name.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <typeparam name="TEvent">CLR event type</typeparam>
    /// <returns>Mapped string event type name</returns>
    public string GetEventTypeName<TEvent>() =>
        GetEventTypeName(typeof(TEvent));

    Type TypeForDotNetName(string dotnetTypeName, string eventTypeName);
}

public class DefaultEventTypeMapper: IEventTypeMapper
{
    public string GetEventTypeName(Type eventType) =>
        eventType.IsGenericType ? eventType.ShortNameInCode() : eventType.Name.ToTableAlias();

    public Type TypeForDotNetName(string dotnetTypeName, string eventTypeName) =>
        Type.GetType(dotnetTypeName);
}


internal class EventTypeMapperWrapper: IEventTypeMapper
{
    private readonly Ref<ImHashMap<Type, string>> _typeToName = Ref.Of(ImHashMap<Type, string>.Empty);
    private readonly Ref<ImHashMap<string, Type>> _nameToType = Ref.Of(ImHashMap<string, Type>.Empty);

    private readonly IEventTypeMapper _eventTypeMapper = new DefaultEventTypeMapper();

    public string GetEventTypeName(Type eventType) =>
        _eventTypeMapper.GetEventTypeName(eventType);

    public Type TypeForDotNetName(string dotnetTypeName, string eventTypeName)
    {
        if (dotnetTypeName.IsEmpty())
        {
            throw new UnknownEventTypeException(eventTypeName);
        }

        if (!_nameToType.Value.TryFind(dotnetTypeName, out var value))
        {
            value = _eventTypeMapper.TypeForDotNetName(dotnetTypeName, eventTypeName);
            if (value == null)
            {
                throw new UnknownEventTypeException($"Unable to load event type '{dotnetTypeName}'.");
            }

            _nameToType.Swap(n => n.AddOrUpdate(dotnetTypeName, value));
        }

        return value;
    }
}



/// <summary>
///     Class <c>EventMappingExtensions</c> exposes extensions and helpers to handle event type mapping.
/// </summary>
public static class EventMappingExtensions
{
    /// <summary>
    ///     Translates by convention the event type name into string event type name and suffix.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <param name="eventTypeName">event type name</param>
    /// <param name="suffix">Type name suffix</param>
    /// <returns>Mapped string event type name in the format: $"{eventTypeName}_{suffix}"</returns>
    public static string GetEventTypeNameWithSuffix(string eventTypeName, string suffix)
    {
        return $"{eventTypeName}_{suffix}";
    }

    /// <summary>
    ///     Translates by convention the CLR type name into string event type name and suffix.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <param name="eventType">CLR event type</param>
    /// <returns>Mapped string event type name with suffix</returns>
    public static string GetEventTypeNameWithSuffix(this IEventTypeMapper eventTypeMapper, Type eventType, string suffix)
    {
        return GetEventTypeNameWithSuffix(eventTypeMapper.GetEventTypeName(eventType), suffix);
    }

    /// <summary>
    ///     Translates by convention the CLR type name into string event type name and suffix.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <typeparam name="TEvent">CLR event type</typeparam>
    /// <returns>Mapped string event type name with suffix</returns>
    public static string GetEventTypeNameWithSuffix<TEvent>(this IEventTypeMapper eventTypeMapper, string suffix)
    {
        return eventTypeMapper.GetEventTypeNameWithSuffix(typeof(TEvent), suffix);
    }

    /// <summary>
    ///     Translates by convention the CLR type name into string event type name with schema version suffix.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <param name="eventType">CLR event type</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <returns>Mapped string event type name with schema version suffix</returns>
    public static string GetEventTypeNameWithSchemaVersion(this IEventTypeMapper eventTypeMapper, Type eventType, uint schemaVersion)
    {
        return eventTypeMapper.GetEventTypeNameWithSuffix(eventType, $"v{schemaVersion}");
    }

    /// <summary>
    ///     Translates by convention the CLR type name into string event type name with schema version suffix.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <typeparam name="TEvent">CLR event type</typeparam>
    /// <param name="schemaVersion">Event schema version</param>
    /// <returns>Mapped string event type name with schema version suffix</returns>
    public static string GetEventTypeNameWithSchemaVersion<TEvent>(this IEventTypeMapper eventTypeMapper, uint schemaVersion)
    {
        return eventTypeMapper.GetEventTypeNameWithSchemaVersion(typeof(TEvent), schemaVersion);
    }

    /// <summary>
    ///     Translates by convention the event type name into string event type name with schema version suffix.
    ///     It can handle both regular and generic types.
    /// </summary>
    /// <param name="eventTypeName">event type name</param>
    /// <param name="schemaVersion">Event schema version</param>
    /// <returns>Mapped string event type name in the format: $"{eventTypeName}_{version}"</returns>
    public static string GetEventTypeNameWithSchemaVersion(string eventTypeName, uint schemaVersion)
    {
        return GetEventTypeNameWithSuffix(eventTypeName, $"v{schemaVersion}");
    }
}
