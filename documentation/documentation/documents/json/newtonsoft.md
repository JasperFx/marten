<!--Title: Serializing with Newtonsoft.Json-->

The default JSON serialization strategy inside of Marten uses [Newtonsoft.Json](http://www.newtonsoft.com/json). We have standardized on Newtonsoft.Json
because of its flexibility and ability to handle polymorphism within child collections. Marten also uses Newtonsoft.Json internally to do JSON diff's for 
the automatic dirty checking option.

TODO(After Corey's done w/ the CoreCLR changes, change the JsonNetSerializer to make it easier to customize)

