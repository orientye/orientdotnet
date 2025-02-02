using Nacos.V2.Common;

namespace CRPC.Mgr.Nacos.Test
{
    public class NacosTest
    {
        public static async Task Test()
        {
            #region nacos init
            NacosMgr.Instance.Init("http://127.0.0.1:8848/", "945cd984-88e8-438c-8bc6-d0d5ccbfefa0", "nacos", "nacos");
            #endregion

            #region regiser service
            string lanip = Util.NetworkHelper.GetLocalIPv4();
            string dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            await NacosMgr.Instance.Registry.RegisterInstance(
                "egotao:fight-srv",
                Constants.DEFAULT_GROUP,
                $"{lanip}",
                7999, 
                new Dictionary<string, string> {
                    { "srvId", "1002" }, 
                    { "startTime", $"{dt}" },
                    { "lanIp", $"{lanip}" }
                });
            #endregion

            #region test
            await CRPC.Test.DoTest();
            #endregion
        }
    }
}
