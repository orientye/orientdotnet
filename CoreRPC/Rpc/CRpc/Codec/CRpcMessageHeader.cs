using DotNetty.Buffers;
using DotNetty.Common.Internal;

namespace CoreRPC.Rpc.CRpc.Codec
{
    public class CRpcMessageHeader
    {
        /**
         * 消息头
         * 格式: 消息状态[2字节] + 响应状态码[4字节] + 序号[8字节] + 模块号[2字节] + 指令码[2字节] + 扩展消息头(可选)[长度2字节+数据]
        */
    
        /** 标准消息头长度 */
        public const int HEADER_LENGTH = 18;

        /**
         * 消息状态
         * @see CRpcMessageState
         */
        private int state;
        /**
         * 响应状态码
         * <pre>
         * 0: 成功
         * 其他: 错误码或业务逻辑状态码
         * </pre>
         */
        private int resultCode;
        /** 序号 */
        private long sn;
        /** 模块 */
        private short module;
        /** 指令 */
        private short command;
        /** 扩展消息头(网关等内部业务逻辑gateway <=> backend server) */
        private byte[] extHeader = EmptyArrays.EmptyBytes;
        /** 压缩解压原始body长度 */
        private int originalBodySize = 0;

        /**
         * 构造方法
         * @param byteBuf 缓冲区
         * @return
         */
        public static CRpcMessageHeader valueOf(IByteBuffer byteBuf)
        {
            if (byteBuf.ReadableBytes < HEADER_LENGTH)
            {
                String info = String.Format("缓冲区可读字节数{0}小于消息头长度{1}", byteBuf.ReadableBytes, HEADER_LENGTH);
                throw new Exception(info);
            }
            CRpcMessageHeader header = new CRpcMessageHeader();
            header.readFrom(byteBuf);
            return header;
        }

        /**
         * 构造方法
         * @param state 消息状态
         * @param resultCode 结果码
         * @param sn 序号
         * @param module 模块
         * @param command 指令
         * @return
         */
        public static CRpcMessageHeader valueOf(short state, int resultCode, long sn, short module, short command)
        {
            CRpcMessageHeader header = new CRpcMessageHeader();
            header.state = state;
            header.resultCode = resultCode;
            header.sn = sn;
            header.module = module;
            header.command = command;
            return header;
        }

        //    public static Header ofCommand(long sn, Command command)
        //    {
        //        Header header = new Header();
        //        header.module = command.getModule();
        //        header.command = command.getCommand();
        //        header.sn = sn;
        //        header.state = command.getState();
        //        return header;
        //    }

        /**
         * 获取头长度
         * 标准长度 + 扩展头 + body压缩原始值
         * @return
         */
        public int getSize()
        {
            return HEADER_LENGTH + (extHeader.Length <=0 ? 0 : extHeader.Length + 2) + (hasState(CRpcMessageState.STATE_COMPRESS) ? 4 : 0);
        }

        //    /** 清除扩展消息头 */
        //    public void clearExtHeader()
        //    {
        //        removeState(CRpcMessageState.STATE_EXT_HEADER);
        //        this.extHeader = EMPTY_BYTES;
        //    }

        //    /**
        //     * 设置扩展消息头
        //     * @param bytes
        //     */
        //    public void setExtHeader(byte[] bytes)
        //    {
        //        if (ArrayUtils.isEmpty(bytes))
        //        {
        //            return;
        //        }
        //        this.extHeader = bytes;
        //        addState(CRpcMessageState.STATE_EXT_HEADER);
        //    }

        /**
         * 是否存在指定状态
         * @param state 当前状态值
         * @param checked 被检查的状态
         * @return
         */
        public static bool hasState(int state, int checkedState)
        {
            return (state & checkedState) == checkedState;
        }

        /**
         * 添加状态
         * @param added 被添加的状态
         */
        public void addState(int added)
        {
            state |= added;
        }

        /**
         * 转换为响应消息头
         * @param resultCode 响应结果码
         */
        public void toResponse(int resultCode)
        {
            short nState = CRpcMessageState.STATE_RESPONSE;
            if (hasState(CRpcMessageState.STATE_EXT_HEADER))
            {
                nState |= CRpcMessageState.STATE_EXT_HEADER;
            }
            if (hasState(CRpcMessageState.NONE_ENCRYPT))
            {
                nState |= CRpcMessageState.NONE_ENCRYPT;
            }
            this.resultCode = resultCode;
            this.state = nState;
        }

        /**
         * 移除状态
         * @param removed 被移除的状态
         */
        void removeState(int removed)
        {
            state &= ~removed;
        }

        /**
         * 是否存在指定状态
         * @param checked 被检查的状态
         * @return
         */
        public bool hasState(int checkedState)
        {
            return hasState(state, checkedState);
        }

        /**
         * 写入ByteBuf
         * @param byteBuf 目标ByteBuf
         */
        public void writeTo(IByteBuffer byteBuf)
        {
            bool hasExtHeader = (extHeader.Length > 0);
            if (hasExtHeader)
            {
                addState(CRpcMessageState.STATE_EXT_HEADER);
            }
            else
            {
                removeState(CRpcMessageState.STATE_EXT_HEADER);
            }

            byteBuf.WriteShort(state);      // 状态
            byteBuf.WriteInt(resultCode);   // 响应码
            byteBuf.WriteLong(sn);          // 序号
            byteBuf.WriteShort(module);     // 模块
            byteBuf.WriteShort(command);    // 指令码

            // 扩展消息头
            if (hasExtHeader)
            {
                byteBuf.WriteShort(extHeader.Length);    // 长度[2字节]
                byteBuf.WriteBytes(extHeader);   // 数据
            }

            // 压缩状态下原始消息体长度
            if (hasState(CRpcMessageState.STATE_COMPRESS))
            {
                byteBuf.WriteInt(originalBodySize);
            }
        }

        //    /** 设置body压缩状态 */
        //    void compressBody(int originalBodySize)
        //    {
        //        addState(CRpcMessageState.STATE_COMPRESS);
        //        this.originalBodySize = originalBodySize;
        //    }

        //    /** 扩展消息头 */
        //    public byte[] getExtHeader()
        //    {
        //        return extHeader;
        //    }

        //    /**
        //     * 消息状态
        //     * @see CRpcMessageState
        //     */
        //    public int getState()
        //    {
        //        return state;
        //    }

        /**
         * 响应状态码
         * @see {@link ResultCode}
         */
        public int getResultCode()
        {
            return resultCode;
        }

        /** 序号 */
        public long getSn()
        {
            return sn;
        }

        /** 模块号 */
        public short getModule()
        {
            return module;
        }

        /** 指令码 */
        public short getCommand()
        {
            return command;
        }

        //    /** 消息体原始长度 */
        //    public int getOriginalBodySize()
        //    {
        //        return originalBodySize;
        //    }

        //    public Command toCommand()
        //    {
        //        return Command.of(module, command, state);
        //    }

        //    @Override
        //public String toString()
        //    {
        //        return LogFormatUtils.format("Header[ST={}, RC={}, SN={}, CMD={}:{}, EXT=[{} - {}]]", state, resultCode, sn, module,
        //                command, extHeader.length, BinaryUtils.byte2Hex(extHeader, StringConstants.SPACE));
        //    }

        /**
         * 从ByteBuf读取
         * @param byteBuf 来源byteBuf
         */
        private void readFrom(IByteBuffer byteBuf)
        {
            this.state = byteBuf.ReadShort();
            this.resultCode = byteBuf.ReadInt();
            this.sn = byteBuf.ReadLong();
            this.module = byteBuf.ReadShort();
            this.command = byteBuf.ReadShort();
        
            // 扩展头数据
            if (hasState(CRpcMessageState.STATE_EXT_HEADER)) {
                int extLength = byteBuf.ReadShort();
                if (extLength > 0) {
                    extHeader = new byte[extLength];
                    byteBuf.ReadBytes(extHeader);
                }
            }
        
            // 压缩前原始消息体长度
            if (hasState(CRpcMessageState.STATE_COMPRESS)) {
                originalBodySize = byteBuf.ReadInt();
            }
        }
    }
}
