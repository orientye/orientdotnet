# CRpc Server Push Design

**Date:** 2026-05-28  
**Status:** Proposed

## Goal

Add a first-class server push mechanism to CRpc so a server can send protobuf messages to a connected client over the existing TCP channel without creating an RPC call, waiting for a client response, or requiring client acknowledgement.

The design keeps ordinary request/response RPC unchanged for callers while adding generated, strongly typed push helpers on the server side and generated push handlers on the client side.

## Context

Current CRpc message flow is request/response only:

- `CRpcClient.CallAsync(...)` creates a request sequence number, stores a pending call, writes a `CRpcMessage`, then waits for a matching response.
- `CRpcServerHandler` receives a request, invokes an `IRpcService`, builds a response with the same sequence number, and writes it back.
- `CRpcMessageHeader` already carries message state, result code, sequence number, service id, and method id.
- Generated client code exposes ordinary RPC calls such as `SayHelloAsync(...)`.
- Generated server code currently uses `XxxBase` for service implementations.

Server push is different from streaming RPC. A push is a one-way notification from server to client. It has no client response, no stream lifecycle, no backpressure contract, and no server-visible client handler result.

## Non-Goals

This design does not include:

- Server streaming, client streaming, or bidirectional streaming RPC.
- Client acknowledgement, delivery receipt, retry, or at-least-once delivery semantics.
- Framework-managed user id, session id, room, or topic subscriptions.
- Backwards compatibility for the old generated `XxxBase` / sealed `XxxClient` naming.
- Supporting arbitrary business code constructing raw push frames.
- Cross-thread business execution outside the owning `CRpcLoop`.

## Decisions

### 1. Use an explicit push message state

Use `CRpcMessageState.STATE_PUSH` (`1 << 2`) and route inbound messages by state:

```text
STATE_RESPONSE -> complete pending CRpcClient.CallAsync
STATE_PUSH     -> dispatch to client push handler
other state    -> protocol error / log path
```

Push messages use the existing CRpc frame and header:

| Header field | Push value |
| --- | --- |
| `state` | `STATE_PUSH` plus existing transport flags such as `NONE_ENCRYPT` |
| `resultCode` | `0` |
| `sn` | `0` in the first version; not used for matching |
| `module` | proto `crpc.service_id` |
| `command` | proto `crpc.method_id` |
| `body` | protobuf payload bytes |

The server writes push messages fire-and-forget. The client never writes a response for `STATE_PUSH`.

### 2. Mark push methods in proto

Add a method option:

```proto
extend google.protobuf.MethodOptions {
  bool server_push = 60003;
}
```

Example:

```proto
service Greeter {
  option (crpc.service_id) = 1000;

  rpc SayHello(HelloRequest) returns (HelloReply) {
    option (crpc.method_id) = 1;
  }

  rpc ServerNotice(ServerNoticePush) returns (google.protobuf.Empty) {
    option (crpc.method_id) = 2;
    option (crpc.server_push) = true;
  }
}
```

Rules:

- Normal methods keep request/response RPC semantics.
- `server_push = true` methods are one-way server-to-client messages.
- Push methods must return `google.protobuf.Empty`.
- `service_id + method_id` is a shared namespace; the generator rejects duplicate method ids in one service.
- Push methods do not generate ordinary client call methods.

### 3. Rename generated server base classes

Generated server implementation bases become `XxxServiceBase`.

This is an intentional breaking change. The generator will not keep the old `XxxBase` alias.

For the example service, users write:

```csharp
public sealed class GreeterService : GreeterServiceBase
{
    protected override CRpcTask<(int, HelloReply)> SayHelloAsync(
        CRpcContext context,
        HelloRequest request)
    {
        // business logic
    }
}
```

`XxxServiceBase` owns both server-side directions for one proto service:

- Receive ordinary client RPC calls.
- Send server push messages for methods marked `server_push = true`.

### 4. Generate client base classes for all services

Generated clients become `XxxClientBase` for all services, not only services with push methods.

Users write their concrete client class:

```csharp
public sealed class GreeterClient : GreeterClientBase
{
    protected override CRpcTask OnPushServerNoticeAsync(
        CRpcPushContext context,
        ServerNoticePush message)
    {
        // handle notification
        return CRpcTask.CompletedTask(context.Loop);
    }
}
```

Existing reference usage stays the same at the call site:

```csharp
var reference = await CRpcReference
    .For<GreeterClient>()
    .Url("crpc://127.0.0.1:7999")
    .ConnectAsync(loop);

var client = reference.Proxy;
var (result, reply) = await client.SayHelloAsync(request);
```

`CRpcReferenceBuilder` creates the concrete client and binds the underlying `IRpcClient` through the generated-client interface.

### 5. Push handlers are virtual no-ops by default

`XxxClientBase` generates one virtual handler per push method:

```csharp
protected virtual CRpcTask OnPushServerNoticeAsync(
    CRpcPushContext context,
    ServerNoticePush message)
{
    return CRpcTask.CompletedTask(context.Loop);
}
```

Business clients override only the push messages they care about.

The generated base also contains client binding and push registration logic:

```csharp
public void BindRpcClient(IRpcClient client)
{
    __client = client;
    __client.RegisterPushHandler(
        1000,
        2,
        ServerNoticePush.Parser,
        __OnPushServerNoticeAsync);
}
```

The generated base implements `ICRpcGeneratedClient`. `CRpcReferenceBuilder` calls `BindRpcClient(...)` after constructing the user-owned concrete client. This makes binding explicit and avoids discovering the `__client` field by reflection for generated clients.

### 6. Push helper methods live on `XxxServiceBase`

`XxxServiceBase` generates protected push helpers for push methods:

```csharp
protected CRpcTask<bool> PushServerNoticeAsync(
    CRpcConnection connection,
    ServerNoticePush message)
{
    return connection.SendPushAsync(1000, 2, message.ToByteArray());
}
```

The `bool` result means the server accepted the push for write on an active connection. It does not mean the client received, decoded, or processed the push.

The helper takes a `CRpcConnection` parameter instead of implicitly using the current request context. This supports both common cases:

- Push to the current request's connection through `context.Connection`.
- Push to a stored connection found through the connection registry.

## Architecture

### Runtime components

```text
CRpcServer
  ├── CRpcConnectionRegistry
  │   ├── TryGet(connectionId, out CRpcConnection)
  │   └── Snapshot()
  └── CRpcServerHandler
      ├── ChannelActive   -> register CRpcConnection
      ├── ChannelInactive -> unregister CRpcConnection
      └── ChannelRead     -> invoke service with CRpcContext.Connection

CRpcConnection
  ├── ConnectionId
  ├── IsActive
  └── SendPushAsync(serviceId, methodId, body)

CRpcClient
  ├── results: Dictionary<long, PendingCall>
  ├── pushHandlers: Dictionary<(ushort serviceId, ushort methodId), PushHandler>
  ├── CallAsync(...)
  ├── RegisterPushHandler(...)
  ├── OnPushException
  └── OnUnhandledPush

ICRpcGeneratedClient
  └── BindRpcClient(IRpcClient client)
```

### Server connection model

`CRpcServer` owns a registry of active CRpc connections. Each accepted DotNetty channel gets one `CRpcConnection` with a monotonically increasing `ConnectionId`.

The registry is loop-owned. Channel lifecycle events originate on DotNetty IO threads and must post to the server `CRpcLoop` before mutating the registry or invoking business callbacks.

First-version public registry API:

```csharp
public bool TryGet(long connectionId, out CRpcConnection connection);

public IReadOnlyList<CRpcConnection> Snapshot();
```

The framework does not map users, sessions, or rooms. Business code can keep its own maps:

```text
userId -> connectionId
roomId -> connectionIds
```

### Server request context

`CRpcContext` carries the current connection:

```csharp
public sealed class CRpcContext : IRpcContext
{
    public CRpcConnection Connection { get; }
}
```

Service methods can push to the caller:

```csharp
protected override async CRpcTask<(int, HelloReply)> SayHelloAsync(
    CRpcContext context,
    HelloRequest request)
{
    await PushServerNoticeAsync(
        context.Connection,
        new ServerNoticePush { Message = "hello" });

    return (0, new HelloReply { Msg = "ok" });
}
```

### Client push dispatch

`CRpcClient` keeps ordinary pending calls and push handlers separate.

When an inbound `CRpcMessage` arrives:

1. If it has `STATE_RESPONSE`, use `sn` to complete a pending call.
2. If it has `STATE_PUSH`, look up a registered handler by `(serviceId, methodId)`.
3. If a handler exists, parse the body with the registered parser and invoke the handler on the client owner loop.
4. If no handler exists, invoke `OnUnhandledPush` or log by default.

Push handler completion is local to the client. The dispatcher does not write any response to the server.

### Error handling

Server push write behavior:

- `PushXxxAsync` returns `false` if the connection is inactive.
- Channel write failures complete as `false`.
- Programming errors such as `null` payloads or using a connection from the wrong `CRpcLoop` throw.
- A `true` result only means the write was submitted to the channel.

Client push behavior:

- Unknown push route: call `OnUnhandledPush` or log; keep the connection open.
- Body parse failure: call `OnPushException`; keep the connection open.
- Handler exception or failed returned `CRpcTask`: call `OnPushException`; keep the connection open.
- Missing override is not unhandled. The generated virtual no-op handler is a valid handler.

### Threading

All CRpc business state remains owned by `CRpcLoop`:

- Server connection registry mutation runs on the server loop.
- `CRpcContext.Connection` is used from service execution on the server loop.
- `CRpcConnection.SendPushAsync` must be called on the owning server loop; calls from other threads throw. External threads must enter through `CRpcLoop.Post`.
- Client push handler dispatch and exception callbacks run on the client owner loop.
- `CRpcTaskCompletionSource.TrySetResult`, `TrySetException`, and `TrySetCanceled` are only called on the owning loop.

DotNetty IO threads do not run business logic directly.

## Generated Code Shape

### `XxxServiceBase`

For ordinary RPC methods:

```csharp
public abstract class GreeterServiceBase : IRpcService, IRpcHttpJsonCodec
{
    public ushort GetServiceId() => 1000;

    public CRpcTask<(int, byte[])> OnMessageAsync(
        IRpcContext context,
        IRpcMessage req)
    {
        // dispatch only ordinary RPC methods
    }

    protected abstract CRpcTask<(int, HelloReply)> SayHelloAsync(
        CRpcContext context,
        HelloRequest request);
}
```

For push methods:

```csharp
protected CRpcTask<bool> PushServerNoticeAsync(
    CRpcConnection connection,
    ServerNoticePush message)
{
    return connection.SendPushAsync(1000, 2, message.ToByteArray());
}
```

Push methods are not included in `OnMessageAsync` dispatch and are not exposed as abstract service methods.

### `XxxClientBase`

Generated client bases include ordinary RPC methods, generated-client binding, and push handlers:

```csharp
public abstract class GreeterClientBase : ICRpcGeneratedClient
{
    public IRpcClient __client = null!;

    public async CRpcTask<(int, HelloReply)> SayHelloAsync(
        HelloRequest request,
        int timeOut = 5000)
    {
        CRpcMessage message = await __client.CallAsync(
            1000,
            1,
            request.ToByteArray(),
            timeOut);

        var result = message.getHeader().getResultCode();
        if (result == 0)
        {
            return (0, HelloReply.Parser.ParseFrom(message.getBody()));
        }

        return (-1, null);
    }

    public void BindRpcClient(IRpcClient client)
    {
        __client = client;
        __client.RegisterPushHandler(
            1000,
            2,
            ServerNoticePush.Parser,
            __OnPushServerNoticeAsync);
    }

    private async CRpcTask __OnPushServerNoticeAsync(
        CRpcPushContext context,
        IMessage message)
    {
        await OnPushServerNoticeAsync(context, (ServerNoticePush)message);
    }

    protected virtual CRpcTask OnPushServerNoticeAsync(
        CRpcPushContext context,
        ServerNoticePush message)
    {
        return CRpcTask.CompletedTask(context.Loop);
    }
}
```

The generated base implements:

```csharp
public interface ICRpcGeneratedClient
{
    void BindRpcClient(IRpcClient client);
}
```

`CRpcProxyActivator` requires generated clients to implement this interface. It creates `TProxy`, casts it to `ICRpcGeneratedClient`, and calls `BindRpcClient(...)`.

## Compatibility and Migration

This design intentionally changes generated API names:

- Old server generated class: `XxxBase`
- New server generated class: `XxxServiceBase`
- Old client generated class: sealed `XxxClient`
- New client generated class: abstract `XxxClientBase`

No obsolete aliases are generated.

Migration examples:

```csharp
// Before
public sealed class GreeterService : GreeterBase
{
}

// After
public sealed class GreeterService : GreeterServiceBase
{
}
```

```csharp
// New user-owned concrete client
public sealed class GreeterClient : GreeterClientBase
{
}
```

Existing call sites can continue to use:

```csharp
CRpcReference.For<GreeterClient>()
```

provided the user-owned concrete client has a public parameterless constructor.

## Tests

Unit coverage should include:

- `CRpcMessage` can encode/decode `STATE_PUSH` without using pending call state.
- `CRpcClient` dispatches `STATE_RESPONSE` to pending calls and `STATE_PUSH` to push handlers.
- `CRpcClient` does not complete or remove pending calls when receiving push messages.
- Unknown push invokes `OnUnhandledPush` or the default log path without closing the connection.
- Push handler exceptions invoke `OnPushException` without closing the connection.
- Generated `XxxClientBase` binds push handlers automatically through `CRpcReferenceBuilder`.
- Generated `XxxServiceBase` excludes push methods from `OnMessageAsync` dispatch.
- `PushXxxAsync` returns `false` for inactive connections.
- `PushXxxAsync` returns `false` for channel write failures.
- `CRpcServerHandler` registers connections on active and unregisters them on inactive.
- Existing ordinary RPC calls still work through `CRpcReference.For<GreeterClient>()`.

Integration coverage should include:

- Start a CRpc server, connect a generated client, call one ordinary RPC, then have the server push one message over the same connection.
- Verify the client receives the push through `OnPushXxxAsync`.
- Verify the server does not wait for, receive, or require any push acknowledgement.

## Implementation Constraints

- Keep `CRpcMessageState.STATE_PUSH = 1 << 2`.
- `CRpcConnection.SendPushAsync` returns `false` for inactive connections and channel write failures, but throws for programmer errors.
- The code generator fails fast when a push method output type is not `google.protobuf.Empty`.
- `CRpcReferenceBuilder` continues requiring `TProxy : class, new()`.
- Generated client bases implement `ICRpcGeneratedClient`; proxy activation uses that interface instead of looking for a `__client` field by reflection.
