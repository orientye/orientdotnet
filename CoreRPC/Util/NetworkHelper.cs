using System.Net.Sockets;
using System.Net;

namespace CoreRPC.Util
{
    public class NetworkHelper
    {
        public static string GetLocalIPv4()
        {
            string ipstr = "127.0.0.1";
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    ipstr = ip.ToString();
                    break;
                }
            }
            return ipstr;
        }
    }
}
