..\protoc.exe --version

..\protoc.exe  --csharp_out=../../WarService/Service --proto_path=../../../protos/protos/  gameserver/fightsrvmsgh5.proto

..\protoc.exe --plugin=protoc-gen-crpc.exe --crpc_out=../../WarService/Service   --proto_path=%userprofile%\.nuget\packages\google.protobuf.tools\3.25.3\tools\ --proto_path=../../../fight-server-h5/CoreRPC/Rpc/CRpc/Protobuf  --proto_path=../../WarService/Protocol  --proto_path=../../../protos/protos/  fightsrvh5.proto

pause



