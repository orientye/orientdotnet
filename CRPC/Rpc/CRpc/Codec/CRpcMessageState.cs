namespace CRpc.Rpc.CRpc.Codec
{
    public class CRpcMessageState
    {
        public const short STATE_NONE = 0;
        /** 压缩 */
        public const short STATE_COMPRESS = 1;

        /** 加密 */
        public const short STATE_ENCRYPT = 1 << 1;

        /** 推送消息 */
        public const short STATE_PUSH = 1 << 2;

        /** 响应消息 */
        public const short STATE_RESPONSE = 1 << 3;

        /**消息体不加密*/
        public const short NONE_ENCRYPT = 1 << 4;

        /** 重置统计数据 */
        public const short STATE_RESET_COUNTER = 1 << 9;

        /** 获取统计数据 */
        public const short STATE_GET_COUNTER = 1 << 10;

        /** 包含扩展消息头 */
        public const short STATE_EXT_HEADER = 1 << 11;

        /** 会话关闭消息 */
        public const short STATE_SESSION_CLOSED = 1 << 12;

        /** PING消息 */
        public const short STATE_PING = 1 << 13;

        /** PONG消息 */
        public const short STATE_PONG = 1 << 14;
    }
}
