..\protoc.exe --version

..\protoc.exe --csharp_out=../../Example/HelloWorld --proto_path=../../Example/HelloWorld helloworld-msg.proto

..\protoc.exe --plugin=protoc-gen-crpc.exe --crpc_out=../../Example/HelloWorld   --proto_path=%userprofile%\.nuget\packages\google.protobuf.tools\3.25.3\tools\ --proto_path=../../../fight-server-h5/CoreRPC/Rpc/CRpc/Protobuf  --proto_path=../../Example/HelloWorld helloworld.proto

copy /Y ..\..\Example\HelloWorld\HelloworldMsg.cs  ..\..\Example\HelloWorld\Server\
move /Y ..\..\Example\HelloWorld\HelloworldService.cs  ..\..\Example\HelloWorld\Server\
move /Y ..\..\Example\HelloWorld\HelloworldMsg.cs  ..\..\Example\HelloWorld\Client\
move /Y ..\..\Example\HelloWorld\HelloworldClient.cs  ..\..\Example\HelloWorld\Client\
pause



