namespace LordUnion.IntegrationTests.Games.TKLord.Replay;

public sealed record XmlBidAction(uint BidScore);

public sealed record XmlPlayAction(string CardString, bool IsPass);
