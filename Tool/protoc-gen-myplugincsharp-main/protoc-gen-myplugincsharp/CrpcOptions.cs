// <auto-generated>
//     Generated by the protocol buffer compiler.  DO NOT EDIT!
//     source: crpc-options.proto
// </auto-generated>
#pragma warning disable 1591, 0612, 3021, 8981
#region Designer generated code

using pb = global::Google.Protobuf;
using pbc = global::Google.Protobuf.Collections;
using pbr = global::Google.Protobuf.Reflection;
using scg = global::System.Collections.Generic;
namespace CRpcOptions {

  /// <summary>Holder for reflection information generated from crpc-options.proto</summary>
  public static partial class CrpcOptionsReflection {

    #region Descriptor
    /// <summary>File descriptor for crpc-options.proto</summary>
    public static pbr::FileDescriptor Descriptor {
      get { return descriptor; }
    }
    private static pbr::FileDescriptor descriptor;

    static CrpcOptionsReflection() {
      byte[] descriptorData = global::System.Convert.FromBase64String(
          string.Concat(
            "ChJjcnBjLW9wdGlvbnMucHJvdG8SBGNycGMaIGdvb2dsZS9wcm90b2J1Zi9k",
            "ZXNjcmlwdG9yLnByb3RvOjUKCnNlcnZpY2VfaWQSHy5nb29nbGUucHJvdG9i",
            "dWYuU2VydmljZU9wdGlvbnMY4dQDIAEoBTozCgltZXRob2RfaWQSHi5nb29n",
            "bGUucHJvdG9idWYuTWV0aG9kT3B0aW9ucxji1AMgASgFQg6qAgtDUnBjT3B0",
            "aW9uc2IGcHJvdG8z"));
      descriptor = pbr::FileDescriptor.FromGeneratedCode(descriptorData,
          new pbr::FileDescriptor[] { global::Google.Protobuf.Reflection.DescriptorReflection.Descriptor, },
          new pbr::GeneratedClrTypeInfo(null, new pb::Extension[] { CrpcOptionsExtensions.ServiceId, CrpcOptionsExtensions.MethodId }, null));
    }
    #endregion

  }
  /// <summary>Holder for extension identifiers generated from the top level of crpc-options.proto</summary>
  public static partial class CrpcOptionsExtensions {
    public static readonly pb::Extension<global::Google.Protobuf.Reflection.ServiceOptions, int> ServiceId =
      new pb::Extension<global::Google.Protobuf.Reflection.ServiceOptions, int>(60001, pb::FieldCodec.ForInt32(480008, 0));
    public static readonly pb::Extension<global::Google.Protobuf.Reflection.MethodOptions, int> MethodId =
      new pb::Extension<global::Google.Protobuf.Reflection.MethodOptions, int>(60002, pb::FieldCodec.ForInt32(480016, 0));
  }

}

#endregion Designer generated code
