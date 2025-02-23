using DotNetty.Buffers;

namespace CRpc.Rpc.CRpc.Codec
{
    public class ChecksumsUtil
    {
        private ChecksumsUtil()
        {
        }

        /**
         * BPHash
         * @param bytes 字节数组
         * @param len 校验长度
         * @return
         */
        public static long BPHash(byte[] bytes, int len)
        {
            if (len <= 0 || bytes.Length < 1)
            {
                return 0;
            }
            long hash = 0;
            for (int i = 0; i < len; i++)
            {
                hash = hash << 7 ^ ((sbyte)bytes[i]);
            }
            return hash;
        }

        /**
         * BPHash
         * @param buf 缓冲区
         * @param len 校验长度
         * @return
         */
        public static long BPHash(IByteBuffer buf, int len)
        {
            if (len <= 0 || buf.ReadableBytes < 1)
            {
                return 0;
            }
            long hash = 0;
            for (int i = 0; i < len; i++)
            {
                hash = hash << 7 ^ (sbyte)(buf.GetByte(i));
            }
            return hash;
        }

        /**
         * BPHash部分校验
         * @param bytes 字节数组
         * @param len 首尾校验字节数(总长度小于 2 * len 则全部参与校验)
         * @return
         */
        public static long BPHashPartly(byte[] bytes, int len)
        {
            if (len <= 0 || bytes.Length < 1)
            {
                return 0;
            }
            if (bytes.Length <= (len << 1))
            {
                return ChecksumsUtil.BPHash(bytes, bytes.Length);
            }

            // head
            long hash = 0;
            for (int i = 0; i < len; i++)
            {
                hash = hash << 7 ^ ((sbyte)bytes[i]);
            }

            // tail
            for (int i = bytes.Length - len; i < bytes.Length; i++)
            {
                hash = hash << 7 ^ ((sbyte)bytes[i]);
            }
            return hash;
        }
    }
}