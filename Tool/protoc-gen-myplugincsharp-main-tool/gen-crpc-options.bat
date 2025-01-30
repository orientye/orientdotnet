..\protoc.exe --version

..\protoc.exe  --proto_path=%userprofile%\.nuget\packages\google.protobuf.tools\3.25.3\tools\ --proto_path=../../../fight-server-h5/CoreRPC/Proto/CRpc/Protobuf  --csharp_out=../protoc-gen-myplugincsharp-main/protoc-gen-myplugincsharp  crpc-options.proto

pause