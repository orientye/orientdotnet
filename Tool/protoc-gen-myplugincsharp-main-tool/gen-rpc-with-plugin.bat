..\protoc.exe --version

..\protoc.exe --csharp_out=. --plugin=protoc-gen-myplugincsharp.exe --myplugincsharp_out=../../WarService/Service/Interface   --proto_path=%userprofile%\.nuget\packages\google.protobuf.tools\3.25.3\tools\ --proto_path=../../../fight-server-h5/CoreRPC/Rpc/CRpc/Protobuf  --proto_path=../../WarService/Protocol  --proto_path=../../../protos/protos/  fightsrvh5.proto

pause




