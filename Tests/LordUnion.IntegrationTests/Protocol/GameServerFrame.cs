namespace LordUnion.IntegrationTests.Protocol;

public readonly record struct GameServerFrame(uint Header0, byte[] Body)
{
    public int BodyLength => Body.Length;
}
