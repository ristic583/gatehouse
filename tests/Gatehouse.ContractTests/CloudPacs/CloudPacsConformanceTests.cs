using Gatehouse.ContractTests.Conformance;

namespace Gatehouse.ContractTests.CloudPacs;

public sealed class CloudPacsConformanceTests(CloudPacsFixture fixture) : PacsAdapterConformanceTests<CloudPacsFixture>(fixture);
