using Nacos.V2;
using Nacos.V2.Common;
using Nacos.V2.Naming.Dtos;

//https://github.com/catcherwong-archive/Nacos2Demo
namespace CoreRPC.Registry.Nacos
{
    public class NacosRegistry
    {
        public INacosNamingService? Srv_ { get; set; }

        #region nameservice
        public async Task RegisterInstance(string serviceName, string groupName, string ip, int port, Dictionary<string, string> metadata)
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

            await Srv_.RegisterInstance(serviceName, groupName, instance);
        }

        async Task DeregisterInstance(string serviceName, string groupName, string ip, int port)
        {
            await Srv_.DeregisterInstance(serviceName, groupName, ip, port);
        }

        async Task<List<Instance>> GetAllInstances(string serviceName, string groupName, bool subscribe)
        {
            var result = await Srv_.GetAllInstances(serviceName, groupName, subscribe);
            return result;
        }

        async Task Subscribe(string serviceName, string groupName, IEventListener listener)
        {
            await Srv_.Subscribe(serviceName, groupName, listener);
        }

        async Task Unsubscribe(string serviceName, string groupName, IEventListener listener)
        {
            await Srv_.Unsubscribe(serviceName, groupName, listener);
        }
        #endregion

        //public static async Task Test()
        //{
        //    var serviceProvider = InitServiceProvider();
        //    INacosNamingService namingSvc = serviceProvider.GetService<INacosNamingService>();

        //    DemoEventListener eventListener = new DemoEventListener();
        //    await RegisterInstance(namingSvc);
        //    await GetAllInstances(namingSvc);
        //    await DeregisterInstance(namingSvc);
        //    await Subscribe(namingSvc, eventListener);
        //}

        //#region 服务相关操作示例
        //static async Task RegisterInstance(INacosNamingService svc, int port = 9999)
        //{
        //    await Task.Delay(500);

        //    var instace = new Nacos.V2.Naming.Dtos.Instance
        //    {
        //        ServiceName = "demo-svc1",
        //        ClusterName = Nacos.V2.Common.Constants.DEFAULT_CLUSTER_NAME,
        //        Ip = "127.0.0.1",
        //        Port = port,
        //        Enabled = true,
        //        Ephemeral = true,
        //        Healthy = true,
        //        Weight = 100,
        //        InstanceId = $"demo-svc1-127.0.0.1-{port}",
        //        Metadata = new System.Collections.Generic.Dictionary<string, string>
        //        {
        //            { "m1", "v1" },
        //            { "m2", "v2" },
        //        }
        //    };

        //    // 注册实例有很多重载，选适合自己的即可。
        //    await svc.RegisterInstance(instace.ServiceName, Nacos.V2.Common.Constants.DEFAULT_GROUP, instace);
        //    Console.WriteLine($"======================注册实例成功");
        //}

        //static async Task GetAllInstances(INacosNamingService svc)
        //{
        //    await Task.Delay(500);

        //    Console.WriteLine("======================准备获取实例");
        //    // 获取全部实例有很多重载，选适合自己的即可。最后一个参数表明要不要订阅这个服务
        //    // SelectInstances, SelectOneHealthyInstance 是另外的方法可以获取服务信息。
        //    var list = await svc.GetAllInstances("demo-svc1", Nacos.V2.Common.Constants.DEFAULT_GROUP, false);
        //    Console.WriteLine($"======================获取实例成功，{Newtonsoft.Json.JsonConvert.SerializeObject(list)}");
        //}

        //static async Task DeregisterInstance(INacosNamingService svc)
        //{
        //    await Task.Delay(500);

        //    // 注销实例有很多重载，选适合自己的即可。
        //    await svc.DeregisterInstance("demo-svc1", Nacos.V2.Common.Constants.DEFAULT_GROUP, "127.0.0.1", 9999);
        //    Console.WriteLine($"======================注销实例成功");
        //}

        //static async Task Subscribe(INacosNamingService svc, IEventListener listener)
        //{
        //    // 订阅服务变化
        //    await svc.Subscribe("demo-svc1", Nacos.V2.Common.Constants.DEFAULT_GROUP, listener);

        //    // 模拟服务变化，listener会收到变更信息
        //    await RegisterInstance(svc, 9997);

        //    await Task.Delay(3000);

        //    // 取消订阅
        //    await svc.Unsubscribe("demo-svc1", Nacos.V2.Common.Constants.DEFAULT_GROUP, listener);

        //    // 服务变化后，listener不会收到变更信息
        //    await RegisterInstance(svc);

        //    await Task.Delay(1000);
        //}

        //class DemoEventListener : IEventListener
        //{
        //    public Task OnEvent(IEvent @event)
        //    {
        //        if (@event is Nacos.V2.Naming.Event.InstancesChangeEvent e)
        //        {
        //            Console.WriteLine($"==========收到服务变更事件=======》{Newtonsoft.Json.JsonConvert.SerializeObject(e)}");
        //        }

        //        return Task.CompletedTask;
        //    }
        //}
        //#endregion
    }
}
