using Gatehouse.Domain.Audit;

namespace Gatehouse.UnitTests.Audit;

public sealed class AuditCanonicalJsonTests
{
    [Fact]
    public void Key_order_and_whitespace_do_not_matter()
    {
        Assert.Equal("{\"a\":\"x\",\"b\":1}", AuditCanonicalJson.Canonicalize("{ \"b\": 1,\n \"a\": \"x\" }"));
        Assert.Equal("{}", AuditCanonicalJson.Canonicalize(null));
    }

    [Fact]
    public void Nested_objects_are_sorted()
    {
        Assert.Equal(
            "{\"a\":[{\"x\":1,\"y\":2}],\"z\":{\"b\":true,\"c\":null}}",
            AuditCanonicalJson.Canonicalize("{\"z\":{\"c\":null,\"b\":true},\"a\":[{\"y\":2,\"x\":1}]}"));
    }

    [Fact]
    public void Non_integer_number_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => AuditCanonicalJson.Canonicalize("{\"a\":1.5}"));
        Assert.Throws<ArgumentException>(() => AuditCanonicalJson.Canonicalize("[1]"));
    }
}
