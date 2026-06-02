using LordUnion.IntegrationTests.Protocol;

namespace LordUnion.IntegrationTests.Tests;

public class LoginAckJsonParserTests
{
    [Fact]
    public void TryGetUserId_ParsesPid()
    {
        const string json = "{\"G_SessionID\":199213504010403840,\"pid\":214291552,\"reg_flag\":0}";

        Assert.Equal(214291552u, LoginAckJsonParser.TryGetUserId(json));
        Assert.Equal(199213504010403840ul, LoginAckJsonParser.TryGetSessionId(json));
    }
}
