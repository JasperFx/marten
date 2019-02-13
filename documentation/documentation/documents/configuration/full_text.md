<!--title:Full Text Indexes-->

Full Text Indexes in Marten are built based on <[linkto:documentation/documents/configuration/gin_or_gist_index;title=Gin Indexes]> utilizing [Postgres built in Text Search functions](https://www.postgresql.org/docs/10/textsearch-controls.html). This enables the possibility to do more sophisticated searching through text fields.

<div class="alert alert-warning">
To use this feature, you will need to use PostgreSQL version 10.0 or above, as this is the first version that support text search function on jsonb column - this is also the data type that Marten use to store it's data.
</div>

## Definining Full Text Index through Store options

Full Text Indexes can be created using the fluent interface of `StoreOptions` like this: 


* one index for whole document - all document properties values will be indexed

<[sample:using_whole_document_full_text_index_through_store_options_with_default]>

<div class="alert alert-info">
If you don't specify language (regConfig) - by default it will be created with 'english' value.
</div>

* single property - there is possibility to specify specific property to be indexed

<[sample:using_a_single_property_full_text_index_through_store_options_with_default]>

* single property with custom settings

<[sample:using_a_single_property_full_text_index_through_store_options_with_custom_settings]>

* multiple properties

<[sample:using_multiple_properties_full_text_index_through_store_options_with_default]>

* multiple properties with custom settings

<[sample:using_multiple_properties_full_text_index_through_store_options_with_custom_settings]>

* more than one index for document with different languages (regConfig)

<[sample:using_more_than_one_full_text_index_through_store_options_with_different_reg_config]>

## Defining Full Text  Index through Attribute

Full Text  Indexes can be created using the `[FullTextIndex]` attribute like this: 

* one index for whole document - by setting attribute on the class all document properties values will be indexed

<[sample:using_a_full_text_index_through_attribute_on_class_with_default]>

* single property

<[sample:using_a_single_property_full_text_index_through_store_options_with_default]>

<div class="alert alert-info">
If you don't specify regConfig - by default it will be created with 'english' value.
</div>

* single property with custom settings

<[sample:using_a_single_property_full_text_index_through_store_options_with_custom_settings]>

* multiple properties

<[sample:using_multiple_properties_full_text_index_through_store_options_with_default]>

<div class="alert alert-info">
To group multiple properties into single index you need to specify the same values in `IndexName` parameters.
</div>

* one index for multiple properties with custom settings

<[sample:using_multiple_properties_full_text_index_through_store_options_with_custom_settings]>

* multiple indexes for multiple properties with custom settings

<[sample:using_more_than_one_full_text_index_through_store_options_with_different_reg_config]>
