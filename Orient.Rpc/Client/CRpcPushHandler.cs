using Orient.Runtime;

namespace Orient.Rpc.Client;

public delegate OrientTask CRpcPushHandler(CRpcPushContext context, byte[] body);
