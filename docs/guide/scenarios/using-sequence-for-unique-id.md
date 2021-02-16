# Using sequences for unique and human-readable identifiers

This scenario demonstrates how to generate unique, human-readable (number) identifiers using Marten and PostgreSQL sequences.

## Scenario

Let us assume we have a system using types with non-human-readable identifiers (e.g. `Guid`) for internal system implementation. However, for end users we want to expose references to the said entities in a human-readable form. Furthermore, we need the identifiers to be unique and from a running positive sequence starting from 10000. This scenario demonstrates how to implement the described behavior using Marten and PostgreSQL sequences.

We first introduce a Marten schema customization type, deriving from `FeatureSchemaBase`:

<<< @/../src/Marten.Schema.Testing/Storage/ScenarioUsingSequenceForUniqueId.cs#sample_scenario-usingsequenceforuniqueid-setup

This sequence yielding customization will be plugged into Marten via the store configuration

<<< @/../src/Marten.Schema.Testing/Storage/ScenarioUsingSequenceForUniqueId.cs#sample_scenario-usingsequenceforuniqueid-storesetup-1

and then executed against the database (generating & executing the DDL statements that create the required database objects):

<<< @/../src/Marten.Schema.Testing/Storage/ScenarioUsingSequenceForUniqueId.cs#sample_scenario-usingsequenceforuniqueid-storesetup-2

We introduce a few types with `Guid` identifiers, whom we reference to our end users by numbers, encapsulated in the `Matter` field:

<<< @/../src/Marten.Schema.Testing/Storage/ScenarioUsingSequenceForUniqueId.cs#sample_scenario-usingsequenceforuniqueid-setup-types

Now, when creating and persisting such types, we first query the database for a new and unique running number. While we generate (or if wanted, let Marten generate) non-human-readable, system-internal identifiers for the created instances, we assign to them the newly generated and unique human-readable identifier:

<<< @/../src/Marten.Schema.Testing/Storage/ScenarioUsingSequenceForUniqueId.cs#sample_scenario-usingsequenceforuniqueid-querymatter

Lastly, we have an extension method (used above) as a shorthand for generating the SQL statement for a sequence value query:

<<< @/../src/Marten.Schema.Testing/Storage/ScenarioUsingSequenceForUniqueId.cs#sample_scenario-usingsequenceforuniqueid-setup-extensions
