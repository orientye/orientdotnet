using System.Diagnostics;
using DotNetty.Buffers;

namespace CRPC.Rpc.CRpc.Codec
{
    public class CRpcMessage : IRpcMessage
    {
        /**
         *   数据包: 包头标识[4字节] + 包长度[4字节] + 通信消息 + 校验hash[4字节]
         *   包长度: 4字节[包头本身长度] + 通信消息长度 + 4字节[校验hash长度]
         *   通信消息: 消息头 + 扩展消息头 + 消息体
         *   消息头: 消息状态[2字节] + 响应状态码[4字节] + 序号[8字节] + 模块号[2字节] + 指令码[2字节]
         *   扩展消息头: 消息头长度[2字节] + 扩展信息
         *   消息体: [压缩前长度]PB二进制
         */
           
        /** 魔数长度, 包长度, 校验位长度 */
        public const int MAGIC = 4, LENGTH = 4, HASH = 4;
        /** 包头标识 - Salute for John D. Carmack */
        public const int MAGIC_NUM = 0x5F3759DF;
        /** 最小数据帧长度(包头[4字节] + 包长度[4字节] + 消息头[18字节] + 校验hash[4字节]) */
        public const int MIN_FRAME_LENGTH = MAGIC + LENGTH + CRpcMessageHeader.HEADER_LENGTH + HASH;
        /** 空字节数组 */
        private static byte[] EMPTY_BYTES = new byte[0];

        /** 消息头 */
        private CRpcMessageHeader header;
        /** 扩展消息头 */
        private CRpcMessageExtHeader extHeader;
        /** 消息体 */
        private byte[] body = EMPTY_BYTES;

        /**
            * 构造方法
            * @param frame 数据帧
            * @return
            */
        public static CRpcMessage valueOf(IByteBuffer frame)
        {
            frame.SkipBytes(MAGIC);     // 跳过包头标识
            int length = frame.ReadInt();   // packet size
            CRpcMessage message = new CRpcMessage();
            message.header = CRpcMessageHeader.valueOf(frame);
            // 消息体长度 = 数据总长度 - 魔数 - 包长度 - 消息头长度 - 校验hash长度
            int bodyLength = length - MAGIC - LENGTH - message.header.getSize() - HASH;
            if (bodyLength > 0)
            {
                message.body = new byte[bodyLength];
                frame.ReadBytes(message.body);
            }
            return message;
        }

        /**
         * 转换为帧数据
         * @param byteBuf 目标ByteBuf
         * @param hashLength 校验首尾字节数
         */
        public void toFrame(IByteBuffer byteBuf, int hashLength)
        {
            byteBuf.WriteInt(MAGIC_NUM);   // 标识
            byteBuf.WriteInt(this.getSize());   // 长度
            header.writeTo(byteBuf);   // 消息头

            // 消息体
            if (body.Length > 0)
            {
                byteBuf.WriteBytes(body);       // 消息体
                int hashSum = (int)ChecksumsUtil.BPHashPartly(body, hashLength);
                byteBuf.WriteInt(hashSum);      // hashsum
            }
            else
            {
                byteBuf.WriteInt(0);
            }
        }

        /**
         * 构造方法
         * @param header 消息头
         * @param body 消息体
         * @return
         */
        public static CRpcMessage valueOf(CRpcMessageHeader header, byte[] body)
        {
            CRpcMessage message = new CRpcMessage();
            message.header = header;
            if (null != body)
            {
                message.body = body;
            }
            return message;
        }

        /** 消息头 */
        public CRpcMessageHeader getHeader()
        {
            return header;
        }
        
        public short getServiceId()
        {
            return header.getModule();
        }
        
        public short getMethodId()
        {
            return header.getCommand();
        }
        
        public long getReqSequence()
        {
            return header.getSn();
        }

        ///** 扩展消息头 */
        //public byte[] getExtHeader()
        //{
        //    return header.getExtHeader();
        //}

        ///** PB扩展消息头 */
        //public ExtHeader getPbExtHeader()
        //{
        //    if (null != extHeader)
        //    {
        //        return extHeader;
        //    }
        //    try
        //    {
        //        extHeader = PbExtHeader.ExtHeader.parseFrom(header.getExtHeader());
        //    }
        //    catch (InvalidProtocolBufferException e)
        //    {
        //        LOGGER.error("ExtHeader反序列化异常", e);
        //        extHeader = PbExtHeader.ExtHeader.getDefaultInstance();
        //    }
        //    return extHeader;
        //}

        ///** 清除扩展消息头 */
        //public void clearExtHeader()
        //{
        //    extHeader = null;
        //    header.clearExtHeader();
        //}

        ///**
        // * 设置扩展消息头
        // * @param bytes 扩展消息头数据
        // */
        //public void setExtHeader(byte[] bytes)
        //{
        //    extHeader = null;
        //    header.setExtHeader(bytes);
        //}

        ///**
        // * 获取解密消息体
        // * @return
        // */
        //public byte[] getDecryptedBody()
        //{
        //    // 编码顺序: 先压缩再加密, 解码顺序: 先解密再解压
        //    if (ArrayUtils.isEmpty(body))
        //    {
        //        return body;
        //    }

        //    // 解密
        //    if (this.hasState(CRpcMessageState.STATE_ENCRYPT))
        //    {
        //        byte[] beforeDecrypt = body;
        //        byte[] key = this.getEncryptkey();
        //        byte[] decrypt = XXTEA.decrypt(body, key);
        //        if (ArrayUtils.isEmpty(decrypt))
        //        {
        //            String info = LogFormatUtils.format("消息体[{}]加密key[{}]解密失败", beforeDecrypt, key);
        //            LOGGER.error(info);
        //            throw new DecodeException(info);
        //        }
        //        this.body = decrypt;
        //        this.header.removeState(CRpcMessageState.STATE_ENCRYPT);
        //        if (LOGGER.isDebugEnabled())
        //        {
        //            LOGGER.debug("消息体解密前长度[{}], 解密后长度[{}], 解密前消息体[{}], 解密后消息体[{}], 解密key[{}]", beforeDecrypt.length,
        //                    decrypt.length, beforeDecrypt, decrypt, key);
        //        }
        //    }

        //    //  解压
        //    if (this.hasState(CRpcMessageState.STATE_COMPRESS))
        //    {
        //        if (body.length < 4)
        //        {
        //            String info = LogFormatUtils.format("消息体[{}]压缩状态长度不正确", body);
        //            LOGGER.error(info);
        //            throw new DecodeException(info);
        //        }
        //        byte[] beforeDecompress = body;
        //        this.body = Zstd.decompress(body, header.getOriginalBodySize());
        //        this.header.removeState(CRpcMessageState.STATE_COMPRESS);
        //        if (LOGGER.isDebugEnabled())
        //        {
        //            LOGGER.warn("消息体解压前长度[{}], 解压后长度[{}], 解压前消息体[{}], 解压后消息体[{}]", beforeDecompress.length,
        //                    body.length, beforeDecompress, body);
        //        }
        //    }
        //    return body;
        //}

        /**
         * 加密和压缩
         * @param compressThreshold 压缩阈值
         * @param encrypt 是否加密
         * @param compressWithoutEncrypt 是否压缩后不再加密
         */
        public void encryptAndCompress(int compressThreshold, bool encrypt, bool compressWithoutEncrypt)
        {
            // 编码顺序: 先压缩再加密, 解码顺序: 先解密再解压
            if (body.Length <= 0)
            {
                return;
            }

            // 压缩
            if (body.Length >= compressThreshold && !hasState(CRpcMessageState.STATE_COMPRESS))
            {
                Debug.Assert(false);
                //TODO:
                // byte[] compress = Zstd.compress(body);
                // if (LOGGER.isDebugEnabled())
                // {
                //     LOGGER.debug("消息体压缩前长度[{}], 压缩后长度[{}], 压缩率[{}%], 压缩前消息体{}, 压缩后消息体{}", body.length,
                //             compress.length, String.format("%.2f", 100 - compress.length * 100.0 / body.length), body, compress);
                // }
                // compressBody(compress);
            }

            // 加密
            if (!hasState(CRpcMessageState.NONE_ENCRYPT) && encrypt && (!hasState(CRpcMessageState.STATE_COMPRESS)
                    || !compressWithoutEncrypt) && !hasState(CRpcMessageState.STATE_ENCRYPT))
            {
                Debug.Assert(false);
                //TODO:
                // byte[] encryptBody = XXTEA.encrypt(body, getEncryptkey());
                // if (LOGGER.isDebugEnabled())
                // {
                //     LOGGER.debug("消息体加密前长度[{}], 加密后长度[{}], 原始消息体{}, 加密后消息体{}", body.length,
                //             encryptBody.length, body, encryptBody);
                // }
                // encryptBody(encryptBody);
            }
        }

        /** 消息体 */
        public byte[] getBody()
        {
            return body;
        }

        /**
         * 设置消息体
         * @param bytes 消息体
         */
        public void setBody(byte[] bytes)
        {
            if (bytes.Length <= 0)
            {
                return;
            }
            this.body = bytes;
        }

        /**
         * 包头[4字节] + 包长度[4字节] + 消息头长度 + 消息体长度 + 校验hash[4字节]
         * @return
         */
        public int getSize()
        {
            return MAGIC + LENGTH + header.getSize() + body.Length + HASH;
        }

        ///**
        // * 添加状态
        // * @param added 被添加的状态
        // */
        //public void addState(int added)
        //{
        //    this.header.addState(added);
        //}

        ///**
        // * 加解密key
        // * <p>状态码[4字节] + SN[8字节] + 模块号[2字节] + 指令码[2字节]</p>
        // * @return
        // */
        //public byte[] getEncryptkey()
        //{
        //    ByteBuf buf = Unpooled.buffer(16);
        //    buf.writeInt(header.getResultCode());   // 状态码
        //    buf.writeLong(header.getSn());          // sn
        //    buf.writeShort(header.getModule());     // 模块号
        //    buf.writeShort(header.getCommand());    // 指令码

        //    // fix: C语言库对fixed_key处理方式与Java,C#库不一致: C语言中对key中第一个0值后续key全部置为0导致实际加密key与Java,C#库不一致
        //    //      所以对加密key中0值置为1规避此问题
        //    for (int i = 0; i < 16; i++)
        //    {
        //        if (0 == buf.getByte(i))
        //        {
        //            buf.setByte(i, 1);
        //        }
        //    }
        //    byte[] bytes = new byte[16];
        //    buf.readBytes(bytes);

        //    ReferenceCountUtil.release(buf);
        //    return bytes;
        //}

        /**
         * 转换为响应消息
         * @param resultCode 响应结果码
         * @return
         */
        public CRpcMessage toResponse(int resultCode)
        {
            this.header.toResponse(resultCode);
            this.body = EMPTY_BYTES;
            return this;
        }

        ///**
        // * 复制消息 会忽略序号sn
        // * @param message
        // * @return
        // */
        //public Message copyToResponse(Message message)
        //{
        //    Header respHeader = message.getHeader();
        //    this.header.toResponse(respHeader.getResultCode());
        //    this.header.addState(respHeader.getState());
        //    this.body = message.body;
        //    if (this.header.hasState(CRpcMessageState.STATE_COMPRESS))
        //    {
        //        this.header.compressBody(respHeader.getOriginalBodySize());
        //    }
        //    if (this.header.hasState(CRpcMessageState.STATE_ENCRYPT))
        //    {
        //        this.body = message.getDecryptedBody();
        //        this.header.removeState(CRpcMessageState.STATE_ENCRYPT);
        //        this.header.removeState(CRpcMessageState.STATE_COMPRESS);
        //    }
        //    return this;
        //}

        /**
         * 是否存在指定状态
         * @param checked 被检查状态
         * @return
         */
        public bool hasState(int check)
        {
            return header.hasState(check);
        }

        //@Override
        //public String toString()
        //{
        //    return LogFormatUtils.format("Message[{}, body[{} - {}]]", header, body.length,
        //            BinaryUtils.byte2Hex(body, StringConstants.SPACE));
        //}

        ///**
        // * 压缩消息体
        // * @param compressed 压缩后的消息体
        // */
        //private void compressBody(byte[] compressed)
        //{
        //    this.header.compressBody(body.length);
        //    this.body = compressed;
        //}

        ///**
        // * 加密消息体
        // * @param encrypt 加密后消息体
        // */
        //private void encryptBody(byte[] encrypt)
        //{
        //    this.header.addState(CRpcMessageState.STATE_ENCRYPT);
        //    this.body = encrypt;
        //}
    }
}
