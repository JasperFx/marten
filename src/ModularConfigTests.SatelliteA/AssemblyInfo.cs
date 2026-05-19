using JasperFx;

// Marker so the Critter Stack scans this assembly for command-line extensions
// and other discovery surfaces. Note: Marten's DiscoverGeneratedEvolvers does
// NOT filter by this attribute (it walks AppDomain.CurrentDomain.GetAssemblies()),
// but the chip-prescribed pattern is to mark every satellite that participates
// in modular Marten composition with it for forward-compat with the broader
// Critter Stack scanning model.
[assembly: JasperFxAssembly]
