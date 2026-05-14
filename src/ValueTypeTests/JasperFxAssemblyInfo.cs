// Opt this assembly into Marten.SourceGenerator's scanning. LimitedDocQuery
// in linq_querying_with_value_types.cs is a compiled query whose runtime
// dispatch needs the registry populated. See #4405.
//
// Separate file from AssemblyInfo.cs because the csproj's
// <Compile Remove="AssemblyInfo.cs" /> excludes that file from compilation.
[assembly: JasperFx.JasperFxAssembly]
