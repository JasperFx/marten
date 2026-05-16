// Opt this assembly into Marten.SourceGenerator's scanning. Some
// DocumentDbTests files declare ICompiledQuery types whose runtime dispatch
// needs the registry populated. See #4405 for the implicit-opt-in contract.
//
// Note: this is a separate file from AssemblyInfo.cs because the csproj's
// <Compile Remove="AssemblyInfo.cs" /> excludes that file from compilation,
// for reasons unrelated to this change.
[assembly: JasperFx.JasperFxAssembly]
