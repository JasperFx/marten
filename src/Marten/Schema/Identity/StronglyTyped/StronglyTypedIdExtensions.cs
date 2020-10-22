using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Npgsql.TypeMapping;

namespace Marten.Schema.Identity.StronglyTyped
{
    public static class StronglyTypedIdExtensions
    {
        private static Func<Type, Type> primitiveTypeFinder;

        public static Func<Type, Type> PrimitiveTypeFinder
        {
            get
            {
                return primitiveTypeFinder ?? DefaultPrimitiveTypeFinder.FindPrimitiveType;
            }
            set
            {
                primitiveTypeFinder = value;
            }
        }

        private static readonly HashSet<Type> seenPrimitives = new HashSet<Type>();

        private static object mapLock = new object();

        public static void UseStronglyTypedId<TId>(this StoreOptions storeOptions,
            bool shouldConfigureJsonNetSerializer = true)
        {
            storeOptions.UseStronglyTypedId(typeof(TId), shouldConfigureJsonNetSerializer);
        }

        public static void UseStronglyTypedId<TId, TPrimitive>(this StoreOptions storeOptions,
            bool shouldConfigureJsonNetSerializer = true)
        {
            storeOptions.UseStronglyTypedId(typeof(TId), typeof(TPrimitive), shouldConfigureJsonNetSerializer);
        }

        public static void UseStronglyTypedId(this StoreOptions storeOptions, Type idType,
            bool shouldConfigureJsonNetSerializer = true)
        {
            var primitiveType = PrimitiveTypeFinder(idType);
            storeOptions.UseStronglyTypedId(idType, primitiveType, shouldConfigureJsonNetSerializer);
        }

        public static void UseStronglyTypedId(this StoreOptions storeOptions, Type idType, Type primitiveType,
            bool shouldConfigureJsonNetSerializer = true)
        {
            // simple locking strategy as this should be called upon store configuration
            lock (mapLock)
            {
                if (!AlreadyMapped(idType))
                {
                    var pgTypeMapping = GetTypeMapping(primitiveType);
                    if (pgTypeMapping == null)
                    {
                        throw new InvalidOperationException(
                            $"Unable to find NpgsqlTypeMapping for {primitiveType.FullName}");
                    }

                    var mappingBuilder = new NpgsqlTypeMappingBuilder
                    {
                        PgTypeName = pgTypeMapping.PgTypeName,
                        NpgsqlDbType = pgTypeMapping.NpgsqlDbType,
                        DbTypes = pgTypeMapping.DbTypes,
                        ClrTypes = pgTypeMapping.ClrTypes.Union(new[] {idType}).ToArray(),
                        InferredDbType = pgTypeMapping.InferredDbType,
                        TypeHandlerFactory = seenPrimitives.Contains(primitiveType)
                            ? pgTypeMapping
                                .TypeHandlerFactory // the existing one will handle the new type automatically
                            : pgTypeMapping.TypeHandlerFactory.ToWrappedPrimitiveHandlerFactory(primitiveType)
                    };
                    NpgsqlConnection.GlobalTypeMapper.AddMapping(mappingBuilder.Build());
                    TypeMappings.RegisterMapping(idType, pgTypeMapping.PgTypeName, pgTypeMapping.NpgsqlDbType);
                    seenPrimitives.Add(primitiveType);
                }

                if (shouldConfigureJsonNetSerializer)
                {
                    var serializer = storeOptions.Serializer();
                    (serializer as JsonNetSerializer)?.Customize(s =>
                    {
                        if (s.Converters.All(jc => jc.GetType() != typeof(StronglyTypedIdJsonNetConverter<,>).MakeGenericType(idType, primitiveType)))
                        {
                            s.Converters.Add(StronglyTypedIdJsonNetConverterFactory.Create(idType, primitiveType));
                        }
                    });
                    storeOptions.Serializer(serializer);
                }
            }
        }

        public static void UseStronglyTypedIds(this StoreOptions storeOptions,
            bool shouldConfigureJsonNetSerializer = true, params Type[] idTypes)
        {
            storeOptions.UseStronglyTypedIds(idTypes, shouldConfigureJsonNetSerializer);
        }

        public static void UseStronglyTypedIds(this StoreOptions storeOptions, IEnumerable<Type> idTypes,
            bool shouldConfigureJsonNetSerializer = true)
        {
            foreach (var idType in idTypes)
            {
                storeOptions.UseStronglyTypedId(idType, shouldConfigureJsonNetSerializer);
            }
        }

        public static void UseStronglyTypedIds(this StoreOptions storeOptions, IDictionary<Type, Type> idTypes,
            bool shouldConfigureJsonNetSerializer = true)
        {
            foreach (var idTypePair in idTypes)
            {
                storeOptions.UseStronglyTypedId(idTypePair.Key, idTypePair.Value, shouldConfigureJsonNetSerializer);
            }
        }

        private static bool AlreadyMapped(Type idType)
        {
            return idType.IsOneOf(typeof(int), typeof(Guid), typeof(long), typeof(string))
                   || TypeMappings.HasTypeMapping(idType);
        }

        private static NpgsqlTypeMapping GetTypeMapping(Type type)
        {
            return NpgsqlConnection
                .GlobalTypeMapper
                .Mappings
                .FirstOrDefault(mapping => mapping.ClrTypes.Contains(type));
        }
    }
}
