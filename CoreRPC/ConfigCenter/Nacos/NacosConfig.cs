using Nacos.V2;

//https://github.com/catcherwong-archive/Nacos2Demo
namespace CoreRPC.ConfigCenter.Nacos
{
    public class NacosConfig
    {
        public INacosConfigService? Srv { get; set; }
        
        async Task<bool> PublishConfig(string dataId, string group, string content)
        {
            if (Srv != null)
            {
                var result = await Srv.PublishConfig(dataId, group, content);
                return result;
            }

            return false;
        }

        async Task<string> GetConfig(string dataId, string group, long timeoutMs = 5000L)
        {
            if (Srv != null)
            {
                var result = await Srv.GetConfig(dataId, group, timeoutMs);
                return result;
            }

            return "";
        }

        async Task<bool> RemoveConfig(string dataId, string group)
        {
            if (Srv != null)
            {
                var result = await Srv.RemoveConfig(dataId, group);
                return result;
            }

            return false;
        }

        async void AddListener(string dataId, string group, IListener listener)
        {
            if (Srv != null)
                await Srv.AddListener(dataId, group, listener);
        }

        async void RemoveListener(string dataId, string group, IListener listener)
        {
            if (Srv != null)
                await Srv.RemoveListener(dataId, group, listener);
        }
    }
}
