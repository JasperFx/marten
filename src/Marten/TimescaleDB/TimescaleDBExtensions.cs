using System;
using System.Linq;
using Marten.TimescaleDB.Internal;
using Weasel.Postgresql;

namespace Marten.TimescaleDB;

public static class TimescaleDBExtensions
{
    /// <summary>
    /// Enable TimescaleDB support for this Marten store. This registers the PostgreSQL
    /// "timescaledb" extension so that <c>CREATE EXTENSION IF NOT EXISTS timescaledb</c> runs
    /// against every database, and wires up any configured hypertables, compression/retention
    /// policies, and continuous aggregates.
    /// </summary>
    /// <remarks>
    /// TimescaleDB is a loadable module: the target PostgreSQL server must list
    /// <c>timescaledb</c> in <c>shared_preload_libraries</c> for the extension to be creatable.
    /// </remarks>
    public static StoreOptions UseTimescaleDB(this StoreOptions opts, Action<TimescaleDBOptions>? configure = null)
    {
        // Register the extension early — ExtendedSchemaObjects are applied before any table,
        // which is exactly what we want for CREATE EXTENSION.
        if (!opts.Storage.ExtendedSchemaObjects.OfType<Extension>()
                .Any(x => x.ExtensionName == "timescaledb"))
        {
            opts.Storage.ExtendedSchemaObjects.Add(new Extension("timescaledb"));
        }

        var timescale = new TimescaleDBOptions(opts);
        configure?.Invoke(timescale);

        // The hypertable DDL must run AFTER the underlying tables exist. Feature schemas registered via
        // Storage.Add() are yielded by StorageFeatures.AllActiveFeatures after the document/event tables,
        // so this is the correct home for create_hypertable / continuous-aggregate objects.
        if (timescale.Targets.Count > 0)
        {
            opts.Storage.Add(new TimescaleDBFeatureSchema(opts, timescale.Targets));
        }

        return opts;
    }
}
