using CRPC.Util;
using Nacos.V2.Common;

namespace CRPC.Mgr.Nacos.Test;

public class NacosTest
{
    public static async Task Test()
    {
        #region nacos init

        NacosMgr.Instance.Init("http://127.0.0.1:8848/", "945cd984-88e8-438c-8bc6-d0d5ccbfefa0", "nacos", "nacos");

        #endregion

        #region regiser service

        var localIPv4 = NetworkHelper.GetLocalIPv4();
        var dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        await NacosMgr.Instance.Registry.RegisterInstance(
            "test-service",
            Constants.DEFAULT_GROUP,
            $"{localIPv4}",
            7999,
            new Dictionary<string, string>
            {
                { "srvId", "1001" },
                { "startTime", $"{dt}" },
                { "lanIp", $"{localIPv4}" }
            });

        #endregion

        #region test

        await CRPC.Test.DoTest();

        #endregion
    }
}