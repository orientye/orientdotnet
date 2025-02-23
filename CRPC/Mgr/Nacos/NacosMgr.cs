using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nacos.V2;
using Nacos.V2.DependencyInjection;

using CRpc.ConfigCenter.Nacos;
using CRpc.Registry.Nacos;

namespace CRpc.Mgr.Nacos
{
    public sealed class NacosMgr
    {
        private static readonly NacosMgr Inst = new NacosMgr();

        static NacosMgr() { }
        private NacosMgr() {
            Config = new NacosConfig();
            Registry = new NacosRegistry();
        }
        public static NacosMgr Instance
        {
            get => Inst;
        }

        public readonly NacosConfig Config;
        public readonly NacosRegistry Registry;

        public void Init(string url, string nameSpace, string userName, string password)
        {
            IServiceCollection services = new ServiceCollection();

            services.AddNacosV2Config(x =>
            {
                x.ServerAddresses = new List<string> { url };
                x.EndPoint = "";
                x.Namespace = nameSpace;

                x.UserName = userName;
                x.Password = password;

                // switch to use http or rpc
                x.ConfigUseRpc = true;
            });

            services.AddNacosV2Naming(x =>
            {
                x.ServerAddresses = new List<string> { url };
                x.EndPoint = "";
                x.Namespace = nameSpace;

                x.UserName = userName;
                x.Password = password;

                // switch to use http or rpc 
                x.NamingUseRpc = true;
            });

            services.AddLogging(builder => { builder.AddConsole(); });

            IServiceProvider serviceProvider = services.BuildServiceProvider();

            Config.Srv = serviceProvider.GetService<INacosConfigService>();
            Registry.Srv = serviceProvider.GetService<INacosNamingService>();
        }
    }
}
