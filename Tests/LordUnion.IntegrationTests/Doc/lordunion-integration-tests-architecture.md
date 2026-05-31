# LordUnion 集成测试架构

> **范围**：`Tests/LordUnion.IntegrationTests` — 三账号 Live/Fake 场景、阶段化客户端、共享 DotNetty IO、与 `CRpcLoop` 的线程边界。  
> **前提**：阅读 [architecture-draft.md](../../../Doc/architecture-draft.md) 中 `CRpcLoop`、IO→Post→Loop 不变量。  
> **设计 spec**：`docs/superpowers/specs/` 下以 `lordunion-*` 开头的文档（阶段 client、cleanup、shared IO 等）。

**Status（2026-05-30）**：阶段化 `LordUnionSessionClient`、cleanup 对称、共享 `IEventLoopGroup`、Live back-to-back 验收已完成。

---

## 1. 目标

LordUnion 集成测试模拟真实客户端生命周期，在 **单业务线程** 上驱动多账号，对接游戏服 `TKMobileReqMsg` / `TKMobileAckMsg` 协议（非 CRPC `serviceId` / `methodId`）。

对外可读性目标：场景读起来像客户端脚本，而不是 send / wait / parse 流水账：

```text
Connect → Login → Cleanup(pre-signup) → Signup → MatchStart → EnterMatch → EnterRound → Game → Cleanup(post-game)
```

多游戏扩展：Login 到 EnterRound 为 **LordUnion 公共阶段**；仅 **Game** 阶段随 `ILordGameVariant` 变化。

---

## 2. 在 orientdotnet 中的位置

```text
CRpc（核心）
  CRpcLoop / CRpcTask / CRpcLoopRunner
  CRpc.Transport
    TcpChannelHost          ← 共享 TCP + DotNetty 生命周期
    LoopInboundHandler      ← IO 线程 → ownerLoop.Post
    IChannelPipelineFactory ← 按协议装配 pipeline

Tests/LordUnion.IntegrationTests（本 harness）
  ThreePlayersOneGameScenario   ← 多账号编排 + 报告
  LordUnionSessionClient        ← 单账号阶段 API（对外 facade）
  EnterMatchFlow / GameFlow / AccountCleanupFlow  ← internal 重协议段
  GameServerDotNettyTransport   ← Live：TcpChannelHost + GameServerPipelineFactory
  FakeGameServerTransport       ← 单元测试：无真实 TCP

CRpc.Rpc.CRpc.Client
  CRpcClient                    ← 同样基于 TcpChannelHost；与 LordUnion 共用传输层，语义层独立
```

LordUnion **不**把游戏服流量伪装成 CRPC RPC；两者仅在 **DotNetty 通道托管** 上复用代码。

---

## 3. 分层结构

| 层 | 类型 | 职责 |
| --- | --- | --- |
| 场景 | `ThreePlayersOneGameScenario` | 三账号阶段顺序、并发相位、`ScenarioReport`、Fake 特例（如 `MatchStartAckFactory`） |
| 阶段客户端 | `LordUnionSessionClient` | 单账号全部阶段 **对外 API**；Connect/Login/Signup 协议循环在 client 内；开赛后进桌/对局/清理委托 internal Flow |
| 内部 Flow | `EnterMatchFlow` | Signup **之后**：等开赛 push、EnterMatch、EnterRound、进度消息捕获 |
| 内部 Flow | `GameFlow` | 对局内 bot / 调度直到 `LordResultAck` |
| 内部 Flow | `AccountCleanupFlow` | unsignup、ExitGame、ExitMatch（**internal**；对外经 `CleanupAsync`） |
| 会话 | `AccountSession` | 单连接状态、消息路由、`WaitForMessageAsync`、sent/received 轨迹 |
| 传输 | `IGameServerTransport` | Live：`GameServerDotNettyTransport`；Fake：`FakeGameServerTransport` |
| 共享 IO | `LordUnionSharedIo` | Live 场景级 **一个** `IEventLoopGroup`（见 §5） |
| 协议 | `ServerProtocolCodec` | 建 `TKMobileReqMsg`、解码 `ProtocolMessage` |
| 游戏差异 | `LordUnionGameProfile` + `ILordGameVariant` | 公共阶段参数 + 对局 ack/req 适配 |
| 报告 | `ReportWriter` / `ScenarioReport` | 控制台摘要 + JSON（含 cleanup、gameEnd `winSeat` 等） |

**注意**：历史上曾有 `LoginFlow` / `SignupFlow` 命名讨论；**当前代码没有这两个类**。Login / Signup 实现在 `LordUnionSessionClient` 私有 `CallAsync` / `SignupOnceAsync` 中。

---

## 4. 阶段 API 与实现归属

`LordUnionSessionClient` 对外阶段（`*StageResult` 定义于 `Scenarios/StageResults.cs`）：

| 阶段 | Client API | 实现位置 |
| --- | --- | --- |
| 连接 | `ConnectAsync` | Client（绑定 transport、安装 EnterMatch 进度 capture） |
| 登录 | `LoginAsync` | Client（AnonymousBrowse + CommonLogin） |
| 清理 | `CleanupAsync` | → `AccountCleanupFlow` |
| 报名 | `SignupAsync` | Client（TourneySignup；失败时可能 unsignup 重试） |
| 开赛 | `WaitForMatchStartAsync` | → `EnterMatchFlow`（**push**，非 request/response） |
| 进赛 | `EnterMatchAsync` | → `EnterMatchFlow` |
| 进桌 | `EnterRoundAsync` | → `EnterMatchFlow` |
| 进桌组合 | `EnterTableAsync` | Client 组合 WaitForMatchStart + EnterMatch + EnterRound |
| 对局 | `PlayGameAsync` | → `GameFlow`（含 config 重载，内部组 bot / policy） |

场景层额外编排（非 Client 独立 API）：

- Signup 前：`CleanupAsync`（`ScenarioRunOptions.SkipAccountCleanup` 可跳过）
- 对局后：best-effort `CleanupAsync`（失败不翻转 scenario 成功）

---

## 5. 线程与 IO 模型

### 5.1 业务线程：一个 `CRpcLoop`

一场 `ThreePlayersOneGameScenario` 运行中，**所有账号共用一个 `CRpcLoop`**：

- `RunHosted` → `CRpcLoopRunner.RunUntilComplete` 在调用线程绑定 loop 并泵送 `Tick`。
- `RunPhaseConcurrentOnLoopAsync` 在 **同一条 loop 线程** 上为每账号启动 async 流程、交错 await，**不是**多业务线程。

不变量与 [architecture-draft.md §9.4](../../../Doc/architecture-draft.md#94-关键不变量重申) 一致：`AccountSession`、Flow 状态、`CRpcTask` 完成仅在 owner loop 上访问。

### 5.2 IO 线程：共享 `IEventLoopGroup`（Live）

**现状（2026-05-30）**：

```text
ThreePlayersOneGameScenario (Live)
  1 × CRpcLoop                          // 业务
  1 × LordUnionSharedIo.EventLoopGroup  // IoThreadCount 可配置，默认 1
  N × GameServerDotNettyTransport         // 每账号一条 channel，借用同一 group
  N × TcpChannelHost                      // ownsEventLoopGroup = false
```

| 模式 | DotNetty IO | 说明 |
| --- | --- | --- |
| Live | `config.Live.IoThreadCount` 条（通常 1–4） | **账号数 N ≠ IO 线程数**；N 条 TCP channel 共享一个 group |
| Fake | 无 | `FakeGameServerTransport`，无 DotNetty |

压测前必须保持此模型；「每连接一个 `MultithreadEventLoopGroup(1)`」已废弃。

### 5.3 入站路径

```text
Socket (IO 线程)
  → GameServerFrameDecoder / GameServerFrameEncoder
  → LoopInboundHandler.ChannelRead
  → TcpChannelHost.PostInboundMessage
  → ownerLoop.Post(() => transport/session 解码)
  → AccountSession / ServerProtocolCodec.HandleInboundMessage
  → WaitForMessageAsync 完成或 push 捕获
```

DotNetty 线程 **不** 直接修改 `AccountSession` 业务字段。

---

## 6. 场景生命周期（Live）

```text
RunHosted(config, options)
  new CRpcLoop()
  CRpcLoopRunner.RunUntilComplete → RunCoreAsync
    try:
      sharedIo = LordUnionSharedIo.FromConfig(config)   // Live only
      factory = LiveScenarioTransportFactory(codec, sharedIo.EventLoopGroup)
      bundles = 3 × (AccountSession + Transport + LordUnionSessionClient)
      RunCoreWithBundlesAsync:
        login (并发 on loop)
        cleanup pre-signup (并发)
        signup (并发)
        enter table / match start (含 Fake 分支)
        verify same table
        play game (GameFlow + bot)
        cleanup post-game (best-effort)
        build ScenarioReport
    finally:
      foreach bundle: DisconnectAsync → Transport.DisposeAsync  // 只关 channel
      sharedIo.DisposeAsync(loop)                               // group shutdown 一次
```

**Shutdown 顺序**（违反会导致 hang 或后连接不可用）：

1. 各账号 `DisconnectAsync` + `GameServerDotNettyTransport.DisposeAsync`（borrowed host **不** shutdown group）
2. `LordUnionSharedIo.DisposeAsync(loop)` — 单次 `ShutdownGracefullyAsync`，在 loop 上泵送直到完成

`TcpChannelHost.DisposeAsync` 对 borrowed group 会在 loop 上泵送 `CloseAsync` 直至完成（真实 TCP 关闭可能异步）。

---

## 7. 传输抽象

### 7.1 `IGameServerTransport`

- `BindIncomingHandler(session, codec)`
- `ConnectAsync` / `DisconnectAsync` / `SendAsync`
- Live 实现：`GameServerDotNettyTransport`（内部 `TcpChannelHost` + `GameServerPipelineFactory`）
- Fake 实现：内存队列，供 `CRPC.Tests` 快速回归

### 7.2 Pipeline（游戏服帧）

`GameServerPipelineFactory` 装配：

```text
game-server-decoder  → GameServerFrameDecoder
game-server-encoder  → GameServerFrameEncoder
loop-ingress         → LoopInboundHandler(host)
```

与 CRPC 客户端 pipeline（`CRpcClientPipelineFactory`）分离；共用 `TcpChannelHost` 壳，**不**共用消息语义。

### 7.3 `TcpChannelHost` 与共享 group

`CRpc/Transport/TcpChannelHost.cs`：

- 构造可选 `sharedEventLoopGroup`；为 null 时自建 group（`CRpcClient` 默认行为）。
- `ownsEventLoopGroup == false` 时：`ShutdownIoAsync` / `DisposeAsync` **不**关闭共享 group。

LordUnion Live 通过 `LordUnionSharedIo` + factory 注入；`CRpcClient` 仍每实例自有 group（单连接客户端模型）。

---

## 8. 配置

`LordUnionTestConfig`（`appsettings.json` / `appsettings.local.json`）主要节：

| 节 | 用途 |
| --- | --- |
| `Server` | 游戏服 host / port |
| `Protocol` | AnonymousBrowse serialId 等 |
| `Match` | tourneyId、gameId、productId |
| `Accounts` | 至少 3 个测试账号 |
| `Timeouts` | 各阶段超时 |
| `Bot` | 出牌策略、节奏 |
| `Output` | JSON 报告目录 |
| `Live` | **`ioThreadCount`**（默认 1，须 > 0） |

示例：

```json
"live": { "ioThreadCount": 1 }
```

---

## 9. 运行与验证

| 用途 | 命令 |
| --- | --- |
| 单元 / Fake | `dotnet test Tests/CRPC.Tests/CRPC.Tests.csproj --filter FullyQualifiedName~LordUnion` |
| Live 单次 | `dotnet run --project Tests/LordUnion.IntegrationTests`（需 local 配置） |
| Live 连续 | `Tests/LordUnion.IntegrationTests/scripts/run-live-back-to-back.ps1 -Runs 2` |

Live 成功标准：三账号完成一局经典斗地主，或报告标明失败阶段；连续运行依赖 signup 前/后 cleanup 清理残留 match 状态。

---

## 10. 扩展新游戏

1. 实现 `ILordGameVariant`（解码 `lord_ack_msg`、构造 `lord_req_msg`、转 `GameEvent`）。
2. 在 `LordUnionGameProfiles` 增加 profile（`GameId`、`ProductId`、`TourneyId`、`MatchPoint` + variant）。
3. 场景构造时传入 variant，或 `ThreePlayersOneGameScenario(codec, variant)`。
4. **不要**复制 Login / Signup / EnterMatch 流程；仅 Game 段变化。

Bot 侧：`IBotPolicy` / `MinimalLandlordBot` 与 variant 配对；经典斗地主以外可能需要新 policy。

---

## 11. 关系图

```text
                    ┌──────────────────────────────────────┐
                    │   ThreePlayersOneGameScenario        │
                    │   1 × CRpcLoop                       │
                    └───────────────┬──────────────────────┘
                                    │
          ┌─────────────────────────┼─────────────────────────┐
          │                         │                         │
    AccountBundle A           AccountBundle B           AccountBundle C
          │                         │                         │
   LordUnionSessionClient    LordUnionSessionClient    LordUnionSessionClient
          │                         │                         │
   AccountSession            AccountSession            AccountSession
          │                         │                         │
 GameServerDotNettyTransport  ...                         ...
          │                         │                         │
   TcpChannelHost (borrowed)  TcpChannelHost            TcpChannelHost
          └─────────────────────────┴─────────────────────────┘
                                    │
                         LordUnionSharedIo
                         1 × IEventLoopGroup
```

```text
LordUnionSessionClient（单账号）
├── ConnectAsync / LoginAsync / SignupAsync     [Client 内联]
├── WaitForMatchStartAsync ──→ EnterMatchFlow
├── EnterMatchAsync / EnterRoundAsync ──→ EnterMatchFlow
├── EnterTableAsync                             [组合]
├── PlayGameAsync ──→ GameFlow ──→ ILordGameVariant
└── CleanupAsync ──→ AccountCleanupFlow
```

---

## 12. 已知限制与后续

| 项 | 说明 |
| --- | --- |
| 账号数 | `ThreePlayersOneGameScenario` 固定取前 3 账号；压测需新 scenario / harness |
| 业务并发 | 单 loop；CPU 密集 bot 与海量账号可能成为瓶颈 |
| 日志 | 成功路径仍可能较 verbose（`PostSignupDiagnosticMonitor`）；quiet 模式未做 |
| 测试合并 | `EnterMatchFlowTests` / `GameFlowTests` 去重（cleanup spec Phase 4） |
| Load harness | N 账号、批次连接、指标 — 见 shared-io spec「Load-test follow-up」 |

---

## 13. 相关文档

| 文档 | 内容 |
| --- | --- |
| [architecture-draft.md](../../../Doc/architecture-draft.md) | CRpcLoop、CRpcClient、线程不变量 |
| [2026-05-27-unified-transport-abstraction-design.md](../../../docs/superpowers/specs/2026-05-27-unified-transport-abstraction-design.md) | TcpChannelHost 引入 |
| [2026-05-29-lordunion-stage-client-design.md](../../../docs/superpowers/specs/2026-05-29-lordunion-stage-client-design.md) | 阶段 client 设计 |
| [2026-05-29-lordunion-cleanup-phases-0-3c-design.md](../../../docs/superpowers/specs/2026-05-29-lordunion-cleanup-phases-0-3c-design.md) | 命名、cleanup internalize、EnterTableAsync |
| [2026-05-30-lordunion-shared-io-and-load-test-prep-design.md](../../../docs/superpowers/specs/2026-05-30-lordunion-shared-io-and-load-test-prep-design.md) | 共享 IO 与 teardown |
