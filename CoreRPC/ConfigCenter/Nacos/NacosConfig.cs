using Nacos.V2;

//https://github.com/catcherwong-archive/Nacos2Demo
namespace CoreRPC.ConfigCenter.Nacos
{
    public class NacosConfig
    {
        public INacosConfigService? Srv_ { get; set; }

        #region config
        async Task<bool> PublishConfig(string dataId, string group, string content)
        {
            var result = await Srv_.PublishConfig(dataId, group, content);
            return result;
        }

        async Task<string> GetConfig(string dataId, string group, long timeoutMs = 5000L)
        {
            var result = await Srv_.GetConfig(dataId, group, timeoutMs);
            return result;
        }

        async Task<bool> RemoveConfig(string dataId, string group)
        {
            var result = await Srv_.RemoveConfig(dataId, group);
            return result;
        }

        async void AddListener(string dataId, string group, IListener listener)
        {
            await Srv_.AddListener(dataId, group, listener);
        }

        async void RemoveListener(string dataId, string group, IListener listener)
        {
            await Srv_.RemoveListener(dataId, group, listener);
        }
        #endregion

        //public async Task Test()
        //{
        //    await PublishConfig(Srv_);
        //    await GetConfig(Srv_);
        //    await RemoveConfig(Srv_);
        //    DemoConfigListener listener = new DemoConfigListener();
        //    await ListenConfig(Srv_, listener);
        //}

        //#region 配置相关操作示例
        //static async Task PublishConfig(INacosConfigService svc)
        //{
        //    var dataId = "demo-dataid";
        //    var group = "demo-group";
        //    var val = "test-value-" + DateTimeOffset.Now.ToUnixTimeSeconds().ToString();

        //    await Task.Delay(500);
        //    var flag = await svc.PublishConfig(dataId, group, val);
        //    Console.WriteLine($"======================发布配置结果，{flag}");
        //}

        //static async Task GetConfig(INacosConfigService svc)
        //{
        //    var dataId = "demo-dataid";
        //    var group = "demo-group";

        //    await Task.Delay(500);
        //    var config = await svc.GetConfig(dataId, group, 5000L);
        //    Console.WriteLine($"======================获取配置结果，{config}");
        //}

        //static async Task RemoveConfig(INacosConfigService svc)
        //{
        //    var dataId = "demo-dataid";
        //    var group = "demo-group";

        //    await Task.Delay(500);
        //    var flag = await svc.RemoveConfig(dataId, group);
        //    Console.WriteLine($"=====================删除配置结果，{flag}");
        //}

        //static async Task ListenConfig(INacosConfigService svc, IListener listener)
        //{
        //    var dataId = "demo-dataid";
        //    var group = "demo-group";

        //    // 添加监听
        //    await svc.AddListener(dataId, group, listener);

        //    await Task.Delay(500);

        //    // 模拟配置变更，listener会收到变更信息
        //    await PublishConfig(svc);

        //    await Task.Delay(500);
        //    await PublishConfig(svc);

        //    await Task.Delay(500);

        //    // 移除监听
        //    await svc.RemoveListener(dataId, group, listener);

        //    // 配置变更后，listener不会收到变更信息
        //    await PublishConfig(svc);
        //}

        //class DemoConfigListener : IListener
        //{
        //    public void ReceiveConfigInfo(string configInfo)
        //    {
        //        Console.WriteLine($"================收到配置变更信息了 ===》{configInfo}");
        //    }
        //}
        //#endregion
    }
}
