﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nacos.V2;
using Nacos.V2.DependencyInjection;

using CoreRPC.ConfigCenter.Nacos;
using CoreRPC.Registry.Nacos;

namespace CoreRPC.Mgr.Nacos
{
    public sealed class NacosMgr
    {
        private static readonly NacosMgr Inst = new NacosMgr();

        static NacosMgr() { }
        private NacosMgr() {
            config = new NacosConfig();
            registry = new NacosRegistry();
        }
        public static NacosMgr Instance
        {
            get => Inst;
        }

        private readonly NacosConfig config;
        private readonly NacosRegistry registry;

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

            config.Srv = serviceProvider.GetService<INacosConfigService>();
            registry.Srv = serviceProvider.GetService<INacosNamingService>();
        }
    }
}
