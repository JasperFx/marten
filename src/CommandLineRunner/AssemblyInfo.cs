// Opt this assembly into Marten.SourceGenerator's scanning. Without it the
// generator would emit nothing and any session.Query(new FindUserByAllTheThings(...))
// call would throw InvalidOperationException at runtime. See #4405 for the
// implicit-opt-in contract.
[assembly: JasperFx.JasperFxAssembly]
