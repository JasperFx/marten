// Opt this assembly into Marten.SourceGenerator's scanning. Some
// DocumentDbTests files declare ICompiledQuery types whose runtime dispatch
// needs the registry populated. See #4405 for the implicit-opt-in contract.
//
// Note: this is a separate file from AssemblyInfo.cs because the build's Init
// target (SetupTestParallelization in build/build.cs) rewrites AssemblyInfo.cs.
[assembly: JasperFx.JasperFxAssembly]
