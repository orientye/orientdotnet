using Nacos.V2;
using Nacos.V2.Common;
using Nacos.V2.Naming.Dtos;

//https://github.com/catcherwong-archive/Nacos2Demo
namespace CRpc.Registry.Nacos
{
    public class NacosRegistry
    {
        public INacosNamingService? Srv { get; set; }
        public async Task RegisterInstance(string serviceName, string groupName, string ip, int port, Dictionary<string, string> metadata)
        {
            if (Srv != null)
            {
                var instance = new Instance
                {
                    ServiceName = serviceName,
                    ClusterName = Constants.DEFAULT_CLUSTER_NAME,
                    Ip = ip,
                    Port = port,
                    Enabled = true,
                    Ephemeral = true,
                    Healthy = true,
                    Weight = 1,
                    InstanceId = $"{serviceName}-{ip}-{port}",
                    Metadata = metadata
                };

                await Srv.RegisterInstance(serviceName, groupName, instance);
            }
        }

        async Task DeregisterInstance(string serviceName, string groupName, string ip, int port)
        {
            if (Srv != null)
                await Srv.DeregisterInstance(serviceName, groupName, ip, port);
        }

        async Task<List<Instance>> GetAllInstances(string serviceName, string groupName, bool subscribe)
        {
            if (Srv != null)
            {
                var result = await Srv.GetAllInstances(serviceName, groupName, subscribe);
                return result;
            }

            return null;
        }

        async Task Subscribe(string serviceName, string groupName, IEventListener listener)
        {
            if (Srv != null)
                await Srv.Subscribe(serviceName, groupName, listener);
        }

        async Task Unsubscribe(string serviceName, string groupName, IEventListener listener)
        {
            if (Srv != null)
                await Srv.Unsubscribe(serviceName, groupName, listener);
        }
    }
}
