<!--Title: Serializing with Newtonsoft.Json-->

The default JSON serialization strategy inside of Marten uses [Newtonsoft.Json](http://www.newtonsoft.com/json). We have standardized on Newtonsoft.Json
because of its flexibility and ability to handle polymorphism within child collections. Marten also uses Newtonsoft.Json internally to do JSON diff's for 
the automatic dirty checking option.

Out of the box, Marten uses this configuration for Newtonsoft.Json:

<[sample:newtonsoft-configuration]>

To customize the Newtonsoft.Json serialization, you need to explicitly supply an instance of Marten's `JsonNetSerializer` as shown below:

<[sample:customize_json_net_serialization]>

<div class="alert alert-info">
You should not override the Newtonsoft.Json <code>ContractResolver</code> with <code>CamelCasePropertyNamesContractResolver</code> for Json Serialization. Newtonsoft.Json by default respects the casing used in property / field names which is typically PascalCase.
This can be overriden to serialize the names to camelCase and Marten will store the JSON in the database as specified by the Newtonsoft.Json settings. However, Marten uses the property / field names casing for its SQL queries and queries are case sensitive and as such, querying will not work correctly. 
</div>

<div class="alert alert-info">
Marten actually has to keep two Newtonsoft.Json serializers, with one being a "clean" Json serializer that omits all Type metadata. The need for two serializers is why
the customization is done with a nested closure so that the same configuration is always applied to both internal <code>JsonSerializer's</code>.
</div>

## Enum Storage

Marten allows how enum values are being stored. By default, they are stored as integers but it is possible to change that to storing them as strings.

To do that you need to change the serialization settings in the `DocumentStore` options.

<[sample:customize_json_net_enum_storage_serialization]>

## Fields Names Casing

Marten by default stores field names "as they are" (C# naming convention is PascalCase for public properties).  

You can have them also automatically formatted to:
- `CamelCase`,
- `snake_case`
by changing the serialization settings in the `DocumentStore` options.

<[sample:customize_json_net_camelcase_casing_serialization]>

<[sample:customize_json_net_snakecase_casing_serialization]>

## Collection Storage

Marten by default stores the collections as strongly typed (so with $type and $value). Because of that and current `MartenQueryable` limitations, it might result in not properly resolved nested collections queries.

You can change collection storage to `AsArray` then custom `JsonConverter` that will store:
- `ICollection<>`,
- `IList<>`,
- `IReadOnlyCollection<>`,
- `IEnumerable<>`
as regular JSON array. 

That improves the nested collections queries handling.

To do that you need to change the serialization settings in the `DocumentStore` options.

<[sample:customize_json_net_snakecase_collectionstorage]>

## Non Public Members Storage

By default `Newtonsoft.Json` only deserializes properties with public setters. 

You can allow deserialisation of properties with non-public setters by changing the serialization settings in the `DocumentStore` options.

<[sample:customize_json_net_snakecase_nonpublicmembersstorage_nonpublicsetters]>


