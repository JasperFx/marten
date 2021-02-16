# Sequential Identifiers with Hilo

The _Hilo_ sequence generation can be customized with either global defaults or document type specific overrides. By default, the Hilo sequence generation in Marten increments by 1 and uses a "maximum lo" number of 1000.

To set different global defaults, use the `StoreOptions.HiloSequenceDefaults` property like this sample:

<<< @/../src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#sample_configuring-global-hilo-defaults

It's also possible to use one sequence with multiple document types by specifying the same "sequence name".

<<< @/../src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#sample_configuring-global-hilo-defaults-sequencename

To override the Hilo configuration for a specific document type, you can decorate the document type with the `[HiloSequence]` attribute
as in this example:

<<< @/../src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#sample_overriding-hilo-with-attribute

You can also use the `MartenRegistry` fluent interface to override the Hilo configuration for a document type as in this example:

<<< @/../src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#sample_overriding-hilo-with-marten-registry

## Set the Identifier Floor

Marten 1.2 adds a convenience method to reset the "floor" of the Hilo sequence for a single document type:

<<< @/../src/Marten.Schema.Testing/Identity/Sequences/hilo_configuration_overrides.cs#sample_ResetHiloSequenceFloor

This functionality was added specifically to aid in importing data from an existing data source. Do note that this functionality simply guarantees
that all new id's assigned for the document type will be higher than the new floor. It is perfectly possible and even likely that there will be some
gaps in the id sequence.
