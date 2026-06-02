using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;

namespace LordUnion.IntegrationTests.Tests;

public class LordAckDescriberTests
{
    [Fact]
    public void Describe_LordResultAck_IncludesWinseatAndScores()
    {
        var ack = new LordAckMsg
        {
            LordresultAckMsg = new LordResultAck
            {
                Winseat = 2,
            },
        };
        ack.LordresultAckMsg.Score.Add(10);
        ack.LordresultAckMsg.Score.Add(-5);
        ack.LordresultAckMsg.Score.Add(-5);

        var description = LordAckDescriber.Describe(ack);

        Assert.Contains("LordResultAck winseat=2", description, StringComparison.Ordinal);
        Assert.Contains("scores=[10,-5,-5]", description, StringComparison.Ordinal);
    }
}
