using System.Text;
using CRpc.Async;
using LordUnion.IntegrationTests.Config;
using LordUnion.IntegrationTests.Flows;
using LordUnion.IntegrationTests.GameVariants;
using LordUnion.IntegrationTests.Protocol;
using LordUnion.IntegrationTests.Protocol.Generated;
using LordUnion.IntegrationTests.Scenarios;
using LordUnion.IntegrationTests.Sessions;

namespace CRPC.Tests.LordUnion;

public sealed class LordUnionSessionClientTests : CrpcTestBase
{
    private readonly ServerProtocolCodec codec = new();

    [Fact]
    public void ConstructorRejectsNullInputs()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();

        Assert.Throws<ArgumentNullException>(() => new LordUnionSessionClient(null!, transport, codec));
        Assert.Throws<ArgumentNullException>(() => new LordUnionSessionClient(session, null!, codec));
        Assert.Throws<ArgumentNullException>(() => new LordUnionSessionClient(session, transport, null!));
    }

    [Fact]
    public void ExposesSessionAndAlias()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();

        var client = new LordUnionSessionClient(session, transport, codec);

        Assert.Same(session, client.Session);
        Assert.Equal("player1", client.Alias);
    }

    [Fact]
    public void ConnectAsyncBindsTransportAndSetsConnectedState()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));

            Assert.Equal(AccountSessionState.Connected, session.State);
            transport.DeliverIncomingMessage(new ProtocolMessage
            {
                Header0 = 123,
                Kind = ProtocolMessageKind.Unknown,
            });

            await CRpcTask.Delay(1, loop);
            Assert.Single(session.ReceivedMessages);
        });
    }

    [Fact]
    public void LoginAsync_CompletesBrowseAndLogin()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext
                {
                    AccountAlias = session.Alias,
                    Phase = session.CurrentPhase,
                });

            if (sent.Kind == ProtocolMessageKind.AnonymousBrowseReq)
            {
                transport.DeliverIncomingMessage(CreateAnonymousBrowseAck(header0: 3001, aesKey: string.Empty));
            }
            else if (sent.Kind == ProtocolMessageKind.CommonLoginReq)
            {
                transport.DeliverIncomingMessage(CreateCommonLoginAck(header0: 3002, userId: 214291552, nickname: "player-one"));
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));

            var result = await client.LoginAsync(
                new AccountConfig
                {
                    Alias = "player1",
                    Username = "user",
                    Password = "pass",
                },
                new ProtocolConfig
                {
                    AppId = 2,
                    AnonymousSerialId = 1,
                    LoginType = 2,
                },
                TimeSpan.FromSeconds(5));

            Assert.Equal(0, result.Result);
            Assert.Equal(214291552u, result.UserId);
            Assert.Equal("player-one", result.Nickname);
            Assert.Equal(LobbyAes128Crypto.DefaultKey, result.AesKey);
            Assert.Equal(AccountSessionState.LoggedIn, session.State);
            Assert.Equal(214291552u, session.UserId);
            Assert.Equal("player-one", session.Nickname);
            Assert.Equal(3001u, session.AnonymousRouteId);
            Assert.Equal(3002u, session.LoginRouteId);
            Assert.Equal(2, session.SentMessages.Count);
        });
    }

    [Fact]
    public void SignupAsync_SendsProfileParameters()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec)
        {
            UserId = 214291552,
            Nickname = "player-one",
        };
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);
        ProtocolMessage? signupRequest = null;

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext
                {
                    AccountAlias = session.Alias,
                    Phase = session.CurrentPhase,
                });
            if (sent.Kind == ProtocolMessageKind.TourneySignupReq)
            {
                signupRequest = sent;
                transport.DeliverIncomingMessage(CreateTourneySignupAck(
                    tourneyId: 159740,
                    matchPoint: 2008280,
                    gameId: 1001));
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.LoggedIn);
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
            session.SetState(AccountSessionState.LoggedIn);

            var result = await client.SignupAsync(
                new LordUnionGameProfile
                {
                    ProfileId = "classic",
                    GameId = 1001,
                    ProductId = 2008280,
                    TourneyId = 159740,
                    MatchPoint = 2008280,
                    Variant = new ClassicLordVariant(),
                },
                TimeSpan.FromSeconds(5));

            Assert.NotNull(signupRequest);
            Assert.Equal(ProtocolMessageKind.TourneySignupReq, signupRequest!.Kind);
            Assert.Equal(0, result.MobileResult);
            Assert.Equal(0u, result.SignupAckParam);
            Assert.Equal(159740u, result.TourneyId);
            Assert.Equal(2008280u, result.MatchPoint);
            Assert.Equal(1001u, result.GameId);
            Assert.Equal(159740u, session.TourneyId);
            Assert.Equal(2008280u, session.MatchPoint);
            Assert.Equal(AccountSessionState.SignedUp, session.State);
        });
    }

    [Fact]
    public void SignupAsync_FailsWhenMobileParamNonZeroWithoutMatchStart()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });
            if (sent.Kind == ProtocolMessageKind.TourneySignupReq)
            {
                transport.DeliverIncomingMessage(CreateTourneySignupAck(
                    tourneyId: 159740,
                    matchPoint: 2008280,
                    gameId: 1001,
                    mobileParam: 6));
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        var exception = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = 214291552;

            try
            {
                await client.SignupAsync(
                    new LordUnionGameProfile
                    {
                        ProfileId = "classic",
                        GameId = 1001,
                        ProductId = 2008280,
                        TourneyId = 159740,
                        MatchPoint = 2008280,
                        Variant = new ClassicLordVariant(),
                    },
                    TimeSpan.FromSeconds(5),
                    allowUnsignupRetry: false);
                throw new InvalidOperationException("Expected signup failure.");
            }
            catch (InvalidOperationException signupException)
            {
                return signupException;
            }
        });

        Assert.Contains("mobile.param=6", exception.Message, StringComparison.Ordinal);
        Assert.Contains("StartGameClientAck or StartClientExAck", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AccountSessionState.Failed, session.State);
    }

    [Fact]
    public void SignupAsync_CapturesEmbeddedStartClientExFromCombinedSignupAck()
    {
        const uint userId = 214291552;
        const uint matchId = 475051244;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });
            if (sent.Kind == ProtocolMessageKind.TourneySignupReq)
            {
                transport.DeliverIncomingMessage(CreateCombinedSignupAndStartClientExAck(
                    userId,
                    matchId,
                    tourneyId: 159740,
                    matchPoint: 2008280,
                    gameId: 1001,
                    ticket: Encoding.UTF8.GetBytes("test-ticket")));
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
            session.SetState(AccountSessionState.LoggedIn);
            session.UserId = userId;

            var signup = await client.SignupAsync(
                new LordUnionGameProfile
                {
                    ProfileId = "classic",
                    GameId = 1001,
                    ProductId = 2008280,
                    TourneyId = 159740,
                    MatchPoint = 2008280,
                    Variant = new ClassicLordVariant(),
                },
                TimeSpan.FromSeconds(5),
                allowUnsignupRetry: false);

            Assert.Equal(0, signup.MobileResult);
            Assert.Equal(0u, signup.SignupAckParam);

            var matchStart = await client.WaitForMatchStartAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(matchId, matchStart.MatchId);
        });
    }

    [Fact]
    public void WaitForMatchStartAsync_CompletesFromStartClientExAck()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            var waitTask = client.WaitForMatchStartAsync(TimeSpan.FromSeconds(5));

            transport.DeliverIncomingMessage(CreateStartClientExAck(
                userId: 214291552,
                matchId: 475051269,
                tourneyId: 159740,
                productId: 2008280,
                gameId: 1001,
                ticket: Encoding.UTF8.GetBytes("test-ticket"),
                ip: "127.0.0.1",
                port: 30301));

            var result = await waitTask;

            Assert.Equal(475051269u, result.MatchId);
            Assert.Equal("127.0.0.1", result.ServerIp);
            Assert.Equal(30301u, result.ServerPort);
            Assert.Equal(475051269u, session.MatchId);
        });
    }

    [Fact]
    public void WaitForMatchStartAsync_CompletesFromAckCapturedAtConnect()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec);
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));

            transport.DeliverIncomingMessage(CreateStartClientExAck(
                userId: 214291552,
                matchId: 475051269,
                tourneyId: 159740,
                productId: 2008280,
                gameId: 1001,
                ticket: Encoding.UTF8.GetBytes("test-ticket"),
                ip: "127.0.0.1",
                port: 30301));

            await CRpcTask.Delay(1, loop);
            session.SetState(AccountSessionState.SignedUp);

            var result = await client.WaitForMatchStartAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(475051269u, result.MatchId);
            Assert.Equal("127.0.0.1", result.ServerIp);
            Assert.Equal(30301u, result.ServerPort);
        });
    }

    [Fact]
    public void EnterMatchAsync_SendsEnterMatchRequest()
    {
        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec)
        {
            UserId = 214291552,
        };
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);
        var profile = new LordUnionGameProfile
        {
            ProfileId = "classic",
            GameId = 1001,
            ProductId = 2008280,
            TourneyId = 159740,
            MatchPoint = 2008280,
            Variant = new ClassicLordVariant(),
        };

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext
                {
                    AccountAlias = session.Alias,
                    Phase = session.CurrentPhase,
                });

            if (sent.Kind == ProtocolMessageKind.EnterMatchReq)
            {
                transport.DeliverIncomingMessage(CreateEnterMatchAck(475051269, 159740));
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            session.SetState(AccountSessionState.SignedUp);
            transport.BindIncomingHandler(session, codec);

            var result = await client.EnterMatchAsync(
                profile,
                new MatchStartStageResult(475051269, "127.0.0.1", 30301),
                TimeSpan.FromSeconds(5));

            Assert.Equal(475051269u, result.MatchId);
            Assert.Equal(475051269u, result.TableId);
            Assert.Equal(AccountSessionState.EnteringMatch, session.State);
        });
    }

    [Fact]
    public void CleanupAsync_PreSignup_SendsUnsignupThroughClientTransport()
    {
        const uint userId = 214291552;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec)
        {
            UserId = userId,
            Nickname = "player-one",
        };
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

            if (sent.Kind == ProtocolMessageKind.TourneyUnsignupReq)
            {
                transport.DeliverIncomingMessage(CreateTourneyUnsignupAck(
                    tourneyId: 159740,
                    matchPoint: 2008280,
                    param: 0));
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
            session.SetState(AccountSessionState.LoggedIn);

            return await client.CleanupAsync(CreateMatchConfig(), AccountCleanupRunOptions.PreSignup(0));
        });

        Assert.True(result.UnsignupSent);
        Assert.True(result.UnsignupAckReceived);
        Assert.Equal(0u, result.UnsignupParam);
        Assert.Equal(AccountSessionState.LoggedIn, session.State);
    }

    [Fact]
    public void CleanupAsync_PostGame_AllowsFinishedStateAndKnownMatchId()
    {
        const uint matchId = 475051269;

        var loop = new CRpcLoop();
        var session = new AccountSession(loop, "player1", codec)
        {
            UserId = 214291552,
            MatchId = matchId,
        };
        var transport = new FakeGameServerTransport();
        var client = new LordUnionSessionClient(session, transport, codec);

        transport.OnPacketSentAsync = (packet, packetLoop) =>
        {
            var sent = transport.DecodeSentPacket(
                packet,
                new ProtocolDecodeContext { AccountAlias = session.Alias, Phase = session.CurrentPhase });

            switch (sent.Kind)
            {
                case ProtocolMessageKind.TourneyUnsignupReq:
                    transport.DeliverIncomingMessage(CreateTourneyUnsignupAck(
                        tourneyId: 159740,
                        matchPoint: 2008280,
                        param: 0));
                    break;
                case ProtocolMessageKind.ExitGameReq:
                    transport.DeliverIncomingMessage(CreateExitGameAck(matchId));
                    break;
            }

            return CRpcTask.CompletedTask(packetLoop);
        };

        var result = CRpcLoopRunner.RunUntilComplete(loop, async () =>
        {
            await client.ConnectAsync(new ServerConfig(), TimeSpan.FromSeconds(1));
            session.SetState(AccountSessionState.Finished);

            return await client.CleanupAsync(
                CreateMatchConfig(),
                AccountCleanupRunOptions.PostGameCleanup(matchId));
        });

        Assert.Contains(matchId, result.DiscoveredMatchIds);
        Assert.Contains(matchId, result.ExitGameAttemptedMatchIds);
        Assert.Contains(matchId, result.ExitMatchAttemptedMatchIds);
    }

    private static MatchConfig CreateMatchConfig() =>
        new()
        {
            GameId = 1001,
            ProductId = 2008280,
            TourneyId = 159740,
        };

    private static ProtocolMessage CreateTourneyUnsignupAck(uint tourneyId, uint matchPoint, uint param) =>
        new()
        {
            Kind = ProtocolMessageKind.TourneyUnsignupAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneyunsignupAckMsg = new TourneyUnsignupAck
                    {
                        Tourneyid = tourneyId,
                        Matchpoint = matchPoint,
                        Param = param,
                    },
                },
            },
        };

    private static ProtocolMessage CreateExitGameAck(uint matchId) =>
        new()
        {
            Kind = ProtocolMessageKind.ExitGameAck,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    Matchid = matchId,
                    ExitgameAckMsg = new ExitGameAck(),
                },
            },
        };

    private static ProtocolMessage CreateAnonymousBrowseAck(uint header0, string aesKey)
    {
        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.AnonymousBrowseAck,
            Param = 0,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    AnonymousAckMsg = new AnonymousBrowseAck
                    {
                        Param = aesKey,
                        U64servertime = 1,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateCommonLoginAck(uint header0, uint userId, string nickname)
    {
        return new ProtocolMessage
        {
            Header0 = header0,
            Kind = ProtocolMessageKind.CommonLoginAck,
            Param = 0,
            Acknowledgement = new TKMobileAckMsg
            {
                Param = 0,
                LobbyAckMsg = new LobbyAckMsg
                {
                    CommonloginAckMsg = new CommonLoginAck
                    {
                        Userinfo = new LcUserInfoEx
                        {
                            Userid = userId,
                            Nickname = nickname,
                        },
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateTourneySignupAck(
        uint tourneyId,
        uint matchPoint,
        int gameId,
        uint mobileParam = 0)
    {
        return new ProtocolMessage
        {
            Header0 = 4001,
            Kind = ProtocolMessageKind.TourneySignupAck,
            Param = mobileParam,
            Acknowledgement = new TKMobileAckMsg
            {
                Param = mobileParam,
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneysignupexAckMsg = new TourneySignupExAck
                    {
                        Param = 0,
                        Tourneyid = tourneyId,
                        Matchpoint = matchPoint,
                        Gameid = gameId,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateCombinedSignupAndStartClientExAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint matchPoint,
        uint gameId,
        byte[] ticket)
    {
        return new ProtocolMessage
        {
            Header0 = 3001,
            Kind = ProtocolMessageKind.TourneySignupAck,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    TourneysignupexAckMsg = new TourneySignupExAck
                    {
                        Tourneyid = tourneyId,
                        Param = 0,
                        Matchpoint = matchPoint,
                        Gameid = (int)gameId,
                    },
                    StartclientexAckMsg = new StartClientExAck
                    {
                        Userid = userId,
                        Tourneyid = tourneyId,
                        Matchid = matchId,
                        Gameid = gameId,
                        Productid = matchPoint,
                        Ticket = ticket,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateStartClientExAck(
        uint userId,
        uint matchId,
        uint tourneyId,
        uint productId,
        uint gameId,
        byte[] ticket,
        string ip,
        uint port)
    {
        return new ProtocolMessage
        {
            Header0 = 5001,
            Kind = ProtocolMessageKind.StartClientExAck,
            Param = 0,
            Acknowledgement = new TKMobileAckMsg
            {
                LobbyAckMsg = new LobbyAckMsg
                {
                    StartclientexAckMsg = new StartClientExAck
                    {
                        Userid = userId,
                        Matchid = matchId,
                        Tourneyid = tourneyId,
                        Productid = productId,
                        Gameid = gameId,
                        Ticket = ticket,
                        Ip = ip,
                        Port = port,
                    },
                },
            },
        };
    }

    private static ProtocolMessage CreateEnterMatchAck(uint matchId, uint tourneyId)
    {
        return new ProtocolMessage
        {
            Header0 = 5002,
            Kind = ProtocolMessageKind.EnterMatchAck,
            Param = 0,
            Acknowledgement = new TKMobileAckMsg
            {
                MatchAckMsg = new MatchAckMsg
                {
                    EntermatchAckMsg = new EnterMatchAck
                    {
                        Matchid = matchId,
                        Tourneyid = tourneyId,
                    },
                },
            },
        };
    }
}
