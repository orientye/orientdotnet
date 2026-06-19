@echo off
setlocal

set PROTOBUF_TOOLS=%userprofile%\.nuget\packages\google.protobuf.tools\3.35.0-rc1
set PROTOC=%PROTOBUF_TOOLS%\tools\windows_x64\protoc.exe
set PROTO_INCLUDE=%PROTOBUF_TOOLS%\tools\include

if not exist "%PROTOC%" (
  echo protoc not found: %PROTOC%
  echo Restore Google.Protobuf.Tools first:
  echo   dotnet restore ..\crpc-protobuf-plugin\CRpcProtobufPlugin\CRpcProtobufPlugin.csproj
  exit /b 1
)

"%PROTOC%" --version

"%PROTOC%" --csharp_out=../../Example/HelloWorld --proto_path=../../Example/HelloWorld helloworld-msg.proto
if errorlevel 1 exit /b 1

"%PROTOC%" --plugin=protoc-gen-crpc.exe --crpc_out=../../Example/HelloWorld --proto_path=%PROTO_INCLUDE% --proto_path=../../CRpc/Rpc/CRpc/Protobuf --proto_path=../../Example/HelloWorld helloworld.proto
if errorlevel 1 exit /b 1

copy /Y ..\..\Example\HelloWorld\HelloworldMsg.cs ..\..\Example\HelloWorld\Server\
move /Y ..\..\Example\HelloWorld\HelloworldService.cs ..\..\Example\HelloWorld\Server\
move /Y ..\..\Example\HelloWorld\HelloworldMsg.cs ..\..\Example\HelloWorld\Client\
move /Y ..\..\Example\HelloWorld\HelloworldClient.cs ..\..\Example\HelloWorld\Client\

echo Done.
pause
