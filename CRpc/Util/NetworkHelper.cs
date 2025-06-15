using System.Net;
using System.Net.Sockets;

namespace CRpc.Util;

public static class NetworkHelper
{
    public static string GetLocalIPv4()
    {
        var ipStr = "127.0.0.1";
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                ipStr = ip.ToString();
                break;
            }
        }
        return ipStr;
    }
}