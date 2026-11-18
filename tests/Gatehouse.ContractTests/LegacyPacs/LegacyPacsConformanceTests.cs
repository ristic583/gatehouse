using Gatehouse.ContractTests.Conformance;

namespace Gatehouse.ContractTests.LegacyPacs;

public sealed class LegacyPacsConformanceTests(LegacyPacsFixture fixture) : PacsAdapterConformanceTests<LegacyPacsFixture>(fixture);
