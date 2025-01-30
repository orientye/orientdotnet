﻿using System;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf.Compiler;
using Google.Protobuf.Reflection;

namespace CRpcProtobufPlugin
{
    public static class CRpcGen
    {
        public static void Generate(CodeGeneratorRequest request, CodeGeneratorResponse response)
        {
            foreach (var fileDescriptorProto in request.ProtoFile)
                try
                {
                    GenerateByProtoFile(fileDescriptorProto, response);
                }
                catch (Exception ex)
                {
                    using (Stream stream = File.Create("./error.txt"))
                    {
                        var err = Encoding.UTF8.GetBytes(ex.Message + ex.StackTrace);
                        stream.Write(err, 0, err.Length);
                    }

                    response.Error += ex.Message;
                }
        }

        private static void GenerateByProtoFile(FileDescriptorProto fileDescriptorProto, CodeGeneratorResponse response)
        {
            GenerateServer(fileDescriptorProto, response);
            GenerateClient(fileDescriptorProto, response);
        }

        private static void GenerateServer(FileDescriptorProto fileDescriptorProto, CodeGeneratorResponse response)
        {
            if (fileDescriptorProto.Service == null || fileDescriptorProto.Service.Count <= 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("// Code generated by crpc-protobuf-plugin. DO NOT EDIT.");
            sb.AppendLine($"// source: {fileDescriptorProto.Name}");

            sb.AppendLine("");
            sb.AppendLine("using Google.Protobuf;");
            sb.AppendLine("using Google.Protobuf.WellKnownTypes;");
            sb.AppendLine("using CoreRPC.Rpc;");
            sb.AppendLine("using CoreRPC.Rpc.CRpc.Codec;");
            sb.AppendLine("using CoreRPC.Rpc.CRpc.Server;");
            sb.AppendLine("");

            var ns = GetFileNamespace(fileDescriptorProto);
            sb.AppendLine("namespace " + ns + " {");

            foreach (var serviceDescriptorProto in fileDescriptorProto.Service)
            {
                sb.AppendLine("");
                GenerateServiceForServer(serviceDescriptorProto, sb);
            }

            sb.AppendLine("}\n");

            var file = new CodeGeneratorResponse.Types.File
            {
                Name = GetFileName(fileDescriptorProto.Name) + "Service.cs",
                Content = sb.ToString()
            };
            response.File.Add(file);
        }

        private static void GenerateClient(FileDescriptorProto fileDescriptorProto, CodeGeneratorResponse response)
        {
            if (fileDescriptorProto.Service == null || fileDescriptorProto.Service.Count <= 0) return;

            var sb = new StringBuilder();
            sb.AppendLine("// Code generated by crpc-protobuf-plugin. DO NOT EDIT.");
            sb.AppendLine($"// source: {fileDescriptorProto.Name}");

            sb.AppendLine("");
            sb.AppendLine("using Google.Protobuf;");
            sb.AppendLine("using Google.Protobuf.WellKnownTypes;");
            sb.AppendLine("using CoreRPC.Rpc;");
            sb.AppendLine("using CoreRPC.Rpc.CRpc.Codec;");
            sb.AppendLine("using CoreRPC.Rpc.CRpc.Client;");
            sb.AppendLine("");

            var ns = GetFileNamespace(fileDescriptorProto);
            sb.AppendLine("namespace " + ns + " {");

            foreach (var serviceDescriptorProto in fileDescriptorProto.Service)
            {
                sb.AppendLine("");
                GenerateServiceForClient(serviceDescriptorProto, sb);
            }

            sb.AppendLine("}");

            var file = new CodeGeneratorResponse.Types.File
            {
                Name = GetFileName(fileDescriptorProto.Name) + "Client.cs",
                Content = sb.ToString()
            };
            response.File.Add(file);
        }

        private static void GenerateServiceForClient(ServiceDescriptorProto service, StringBuilder sb)
        {
            var hasServiceId = service.Options.CustomOptions.TryGetInt32(CRpcOptions.ServiceId, out int serviceId);
            if (!hasServiceId || serviceId <= 0)
                throw new Exception("Service=" + service.Name + " ServiceId NOT FOUND");
            if (serviceId >= ushort.MaxValue)
                throw new Exception("Service=" + service.Name + "ServiceId too large");
            
            sb.AppendLine($"public sealed class {service.Name}Client : IRpcClient");
            sb.AppendLine("{");
            
            foreach (var method in service.Method)
            {
                var hasMsgId = method.Options.CustomOptions.TryGetInt32(CRpcOptions.MethodId, out int msgId);
                if (!hasMsgId || msgId <= 0)
                    throw new Exception("Service" + service.Name + "." + method.Name + " ' MessageId NOT FOUND");
                if (msgId >= ushort.MaxValue)
                    throw new Exception("Service" + service.Name + "." + method.Name + " is too large");
       
                var outType = GetTypeName(method.OutputType);
                var inType = GetTypeName(method.InputType);

                sb.AppendLine($"    public async Task<(int, {outType})> {method.Name}Async({inType} request, int timeOut = 5000)");
                sb.AppendLine( "    {");
                sb.AppendLine( "        var result = 0;");
                sb.AppendLine( "        if (0 == result)");
                sb.AppendLine( "        {");
                sb.AppendLine($"            return (0, {outType}.Parser.ParseFrom(new byte[0]));");
                sb.AppendLine( "        }");
                sb.AppendLine($"        return (-1, new {outType}());");
                sb.AppendLine( "    }");
                sb.AppendLine();
            }

            sb.AppendLine("}");
        }

        private static void GenerateServiceForServer(ServiceDescriptorProto service, StringBuilder sb)
        {
            var hasServiceId = service.Options.CustomOptions.TryGetInt32(CRpcOptions.ServiceId, out int serviceId);
            if (!hasServiceId || serviceId <= 0)
                throw new Exception("Service=" + service.Name + " ServiceId NOT FOUND");
            if (serviceId >= ushort.MaxValue) throw new Exception("Service=" + service.Name + "ServiceId too large");

            sb.AppendLine($"public abstract class {service.Name}Base : IRpcService");
            sb.AppendLine("{");
            
            sb.AppendLine( "    public int GetServiceId()");
            sb.AppendLine( "    {");
            sb.AppendLine($"        return {serviceId};");
            sb.AppendLine( "    }");
            sb.AppendLine();

            var sbMethodCase = new StringBuilder();
            var sbMethodInner = new StringBuilder();
            var sbMethodAbstract = new StringBuilder();
            foreach (var method in service.Method)
            {
                var hasMethodId = method.Options.CustomOptions.TryGetInt32(CRpcOptions.MethodId, out int methodId);
                if (!hasMethodId || methodId <= 0)
                    throw new Exception("Service" + service.Name + "." + method.Name + " ' MethodId NOT FOUND");
                if (methodId >= ushort.MaxValue)
                    throw new Exception("Service" + service.Name + "." + method.Name + " is too large");

                var outType = GetTypeName(method.OutputType);
                var inType = GetTypeName(method.InputType);

                sbMethodInner.AppendLine($"    private async Task<(int, byte[])> __OnMessage{method.Name}Async(CRpcContext context, CRpcMessage req)");
                sbMethodInner.AppendLine("    {");
                sbMethodInner.AppendLine($"        var request = {inType}.Parser.ParseFrom(req.getBody());");
                sbMethodInner.AppendLine($"        var (result, data) = await {method.Name}Async(context, request);");
                sbMethodInner.AppendLine($"        var bytes = data.ToByteArray();");
                sbMethodInner.AppendLine($"        return (result, bytes);");
                sbMethodInner.AppendLine("    }");
                sbMethodInner.AppendLine();

                sbMethodAbstract.AppendLine($"    protected abstract Task<(int, {outType})> {method.Name}Async(CRpcContext context, {inType} request);");

                sbMethodCase.AppendLine($"        if (methodId == {methodId}) {{ return this.__OnMessage{method.Name}Async(rpcContext, rpcReq); }}");
            }
            
            sb.AppendLine("    public Task<(int, byte[])> OnMessageAsync(IRpcContext context, IRpcMessage req)");
            sb.AppendLine("    {");
            sb.AppendLine("        var rpcContext = (CRpcContext)context;");
            sb.AppendLine("        var rpcReq = (CRpcMessage)req;");
            sb.AppendLine("        var methodId = rpcReq.getMethodId();");
            sb.Append(sbMethodCase);
            sb.AppendLine("        return Task.FromResult((-1, Array.Empty<byte>()));");
            sb.AppendLine("    }");
            sb.AppendLine();

            sb.Append(sbMethodInner);
            sb.Append("    // Please implement the following:");
            sb.AppendLine();
            sb.Append(sbMethodAbstract);

            sb.AppendLine("}");
        }

        private static string GetFileNamespace(FileDescriptorProto protofile)
        {
            var ns = protofile.Options.CsharpNamespace;
            if (string.IsNullOrEmpty(ns))
                throw new Exception("" + protofile.Name + ".proto did not set csharp_namespace");
            return ConvertCamelCase(ns);
        }

        private static string GetFileName(string fileProto)
        {
            var str = fileProto.Split('.')[0];
            return ConvertCamelCase(str);
        }

        private static string ConvertCamelCase(string normalName)
        {
            return string.Join("", normalName.Split('_').Select(s => s.Substring(0, 1).ToUpper() + s.Substring(1)));
        }

        private static string GetTypeName(string typeFullName)
        {
            if (typeFullName.StartsWith(".google.protobuf"))
            {
                return ConvertCamelCase(typeFullName.Split('.').Last());
            }
            //TODO: optimize
            // .gameserver.AaBbCc -> GameServer.AaBbCc
            string str = typeFullName.Substring(1, 1).ToUpper() + typeFullName.Substring(2);
            return str;
        }
    }
}