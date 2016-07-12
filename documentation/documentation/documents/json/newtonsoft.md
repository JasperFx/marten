<!--Title: Serializing with Newtonsoft.Json-->

The default JSON serialization strategy inside of Marten uses [Newtonsoft.Json](http://www.newtonsoft.com/json). We have standardized on Newtonsoft.Json
because of its flexibility and ability to handle polymorphism within child collections. Marten also uses Newtonsoft.Json internally to do JSON diff's for 
the automatic dirty checking option.

Out of the box, Marten uses this configuration for Newtonsoft.Json:

<[sample:newtonsoft-configuration]>

To customize the Newtonsoft.Json serialization, you need to explicitly supply an instance of Marten's `JsonNetSerializer` as shown below:

<[sample:customize_json_net_serialization]>

<div class="alert alert-info">
Marten actually has to keep two Newtonsoft.Json serializers, with one being a "clean" Json serializer that omits all Type metadata. The need for two serializers is why
the customization is done with a nested closure so that the same configuration is always applied to both internal <code>JsonSerializer's</code>.
</div>

