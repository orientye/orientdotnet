@echo off
setlocal EnableDelayedExpansion

set ERR=0

set PROTOBUF_TOOLS=%userprofile%\.nuget\packages\google.protobuf.tools\3.35.0-rc1
set PROTOC=%PROTOBUF_TOOLS%\tools\windows_x64\protoc.exe
set PROTO_INCLUDE=%PROTOBUF_TOOLS%\tools\include

if not exist "%PROTOC%" (
  echo ERROR: protoc not found: %PROTOC%
  echo Restore Google.Protobuf.Tools first:
  echo   dotnet restore ..\orient-crpc-plugin\OrientCrpcPlugin\OrientCrpcPlugin.csproj
  set ERR=1
  goto :finish
)

set "STEP=protoc --version"
set "STEP_ERR=protoc --version failed."
call :RunProtoc --version

set "STEP=Generating helloworld-msg.cs ..."
set "STEP_ERR=failed to generate message code from helloworld-msg.proto"
call :RunProtoc --csharp_out=../../Example/HelloWorld --proto_path=../../Example/HelloWorld helloworld-msg.proto

set "STEP=Generating HelloworldService.cs / HelloworldClient.cs ..."
set "STEP_ERR=failed to generate CRpc code from helloworld.proto"
set "STEP_HINT=Check protoc-gen-crpc.exe and protoc-gen-crpc.dll in this folder."
call :RunProtoc --plugin=protoc-gen-crpc.exe --crpc_out=../../Example/HelloWorld --proto_path=%PROTO_INCLUDE% --proto_path=../../Orient.Rpc/Codec --proto_path=../../Example/HelloWorld helloworld.proto

copy /Y ..\..\Example\HelloWorld\HelloworldMsg.cs ..\..\Example\HelloWorld\Server\
move /Y ..\..\Example\HelloWorld\HelloworldService.cs ..\..\Example\HelloWorld\Server\
move /Y ..\..\Example\HelloWorld\HelloworldMsg.cs ..\..\Example\HelloWorld\Client\
move /Y ..\..\Example\HelloWorld\HelloworldClient.cs ..\..\Example\HelloWorld\Client\

echo.
echo Done.
goto :finish

:RunProtoc
echo.
echo %STEP%
"%PROTOC%" %*
if errorlevel 1 (
  echo ERROR: %STEP_ERR%
  if defined STEP_HINT echo %STEP_HINT%
  set ERR=1
  goto :finish
)
set "STEP_HINT="
exit /b 0

:finish
if %ERR%==1 (
  echo.
  echo gen-helloworld failed.
)
pause
exit /b %ERR%
