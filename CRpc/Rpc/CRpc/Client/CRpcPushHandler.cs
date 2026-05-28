using CRpc.Async;

namespace CRpc.Rpc.CRpc.Client;

public delegate CRpcTask CRpcPushHandler(CRpcPushContext context, byte[] body);
