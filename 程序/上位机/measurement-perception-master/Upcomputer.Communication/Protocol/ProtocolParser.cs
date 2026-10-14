using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Core.Models;

namespace Upcomputer.Communication.Protocol
{
    /// <summary>
    /// 通用协议解析器，WiFi和串口共用
    /// </summary>
    public class ProtocolParser
    {
        // 帧边界常量
        private const ushort FRAME_HEADER = ProtocolConstants.FrameHeader;
        private const byte FRAME_TAIL = ProtocolConstants.FrameTail;
        private const int MAX_DATA_LENGTH = ProtocolConstants.MaxDataLength;

        private readonly List<byte> _buffer = new();
        private readonly object _lock = new();

        /// <summary>
        /// 收到完整数据帧时触发
        /// </summary>
        public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

        /// <summary>
        /// 喂入接收到的数据
        /// </summary>
        public void FeedData(byte[] data)
        {
            lock (_lock)
            {
                _buffer.AddRange(data);
                TryParseFrames();
            }
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void ClearBuffer()
        {
            lock (_lock)
            {
                _buffer.Clear();
            }
        }

        /// <summary>
        /// 尝试从缓冲区解析完整帧
        /// </summary>
        private void TryParseFrames()
        {
            while (_buffer.Count >= 7) // 最小帧长：帧头2+长度2+命令1+方向1+CRC2+帧尾1=9? 实际最小=帧头2+长度2+命令1+方向1(数据长度=0)+CRC2+帧尾1=9
            {
                // 查找帧头
                int headerIndex = FindHeader();
                if (headerIndex < 0)
                {
                    // 没找到帧头，清空缓冲区
                    _buffer.Clear();
                    return;
                }

                // 丢弃帧头前的无效数据
                if (headerIndex > 0)
                    _buffer.RemoveRange(0, headerIndex);

                // 检查是否有足够数据读取长度
                if (_buffer.Count < 4) return;

                // 读取数据区长度（小端序）
                ushort dataLength = (ushort)(_buffer[2] | (_buffer[3] << 8));

                // 总帧长 = 帧头(2) + 长度字段(2) + 数据区(dataLength) + CRC(2) + 帧尾(1)
                int totalFrameLength = 2 + 2 + dataLength + 2 + 1;

                if (_buffer.Count < totalFrameLength) return;

                // 验证帧尾
                if (_buffer[totalFrameLength - 1] != FRAME_TAIL)
                {
                    _buffer.RemoveAt(0);
                    continue;
                }

                // 提取帧数据
                byte[] frameData = _buffer.GetRange(0, totalFrameLength).ToArray();
                _buffer.RemoveRange(0, totalFrameLength);

                // 解析并验证
                if (TryParseFrame(frameData, out var frame))
                {
                    FrameReceived?.Invoke(this, new FrameReceivedEventArgs(frame));
                }
            }
        }

        private int FindHeader()
        {
            for (int i = 0; i < _buffer.Count - 1; i++)
            {
                if (_buffer[i] == 0xAA && _buffer[i + 1] == 0x55)
                    return i;
            }
            return -1;
        }

        private bool TryParseFrame(byte[] frameData, out ParsedFrame frame)
        {
            frame = new ParsedFrame();

            // 提取字段
            ushort dataLength = (ushort)(frameData[2] | (frameData[3] << 8));
            frame.Command = frameData[4];
            frame.Direction = frameData[5];

            int dataStart = 6;
            int payloadLength = dataLength - 2;
            if (payloadLength > 0)
            {
                frame.Data = new byte[payloadLength];
                Array.Copy(frameData, dataStart, frame.Data, 0, payloadLength);
            }
            else
            {
                frame.Data = Array.Empty<byte>();
            }

            // 提取接收到的CRC
            int crcPos = frameData.Length - 3;
            ushort receivedCrc = (ushort)(frameData[crcPos] | (frameData[crcPos + 1] << 8));

            // 计算期望的CRC（从命令字节到数据区结束）
            int crcDataLength = dataLength; // 命令+方向+数据区 (dataLength 已经包含了命令1和方向1)
            byte[] crcData = new byte[crcDataLength];
            Array.Copy(frameData, 4, crcData, 0, crcDataLength);
            ushort calculatedCrc = Crc16Modbus(crcData);

            if (receivedCrc != calculatedCrc)
            {
                System.Diagnostics.Debug.WriteLine($"[ProtocolParser] CRC校验失败! 接收:{receivedCrc:X4}, 计算:{calculatedCrc:X4}, 长度:{frameData.Length}");
                return false;
            }

            frame.RawData = frameData;
            return true;
        }

        /// <summary>
        /// 打包数据帧（不带时间戳）
        /// </summary>
        public static byte[] PackFrame(byte command, byte direction, byte[]? data = null)
        {
            data ??= Array.Empty<byte>();

            if (data.Length > MAX_DATA_LENGTH)
                throw new ArgumentException($"数据长度超过{MAX_DATA_LENGTH}字节");

            // 计算数据区长度（命令1+方向1+实际数据）
            ushort dataAreaLength = (ushort)(2 + data.Length);

            // 构建CRC计算数据区
            byte[] crcData = new byte[dataAreaLength];
            crcData[0] = command;
            crcData[1] = direction;
            Array.Copy(data, 0, crcData, 2, data.Length);

            ushort crc = Crc16Modbus(crcData);

            // 构建完整帧
            var frame = new List<byte>
        {
            0xAA, 0x55,                          // 帧头
            (byte)(dataAreaLength & 0xFF),       // 长度低字节
            (byte)(dataAreaLength >> 8),         // 长度高字节
            command,
            direction
        };
            frame.AddRange(data);
            frame.Add((byte)(crc & 0xFF));
            frame.Add((byte)(crc >> 8));
            frame.Add(FRAME_TAIL);

            return frame.ToArray();
        }

        /// <summary>
        /// 打包带时间戳的数据帧（有数据帧统一带时间戳，无数据帧不带时间戳）
        /// 帧格式: H1(1) H2(1) dataLen(2) cmd(1) dir(1) [timestamp_flag(1) + timestamp(6)] payload... CRC(2) tail(1)
        /// </summary>
        public static byte[] PackFrameWithTimestamp(byte command, byte direction, byte[]? data = null)
        {
            data ??= Array.Empty<byte>();

            // 无数据帧（ACK/NAK/心跳）不带时间戳
            if (data.Length == 0)
            {
                return PackFrame(command, direction, data);
            }

            // 有数据帧：带时间戳
            // 时间戳标记(1字节, 0x01) + 时间戳(8字节小端序 UTC+8 毫秒)
            long timestampMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 28800L * 1000L;
            byte[] timestampBytes = new byte[8];
            timestampBytes[0] = (byte)(timestampMillis & 0xFF);
            timestampBytes[1] = (byte)((timestampMillis >> 8) & 0xFF);
            timestampBytes[2] = (byte)((timestampMillis >> 16) & 0xFF);
            timestampBytes[3] = (byte)((timestampMillis >> 24) & 0xFF);
            timestampBytes[4] = (byte)((timestampMillis >> 32) & 0xFF);
            timestampBytes[5] = (byte)((timestampMillis >> 40) & 0xFF);
            timestampBytes[6] = (byte)((timestampMillis >> 48) & 0xFF);
            timestampBytes[7] = (byte)((timestampMillis >> 56) & 0xFF);

            if (data.Length > MAX_DATA_LENGTH)
                throw new ArgumentException($"数据长度超过{MAX_DATA_LENGTH}字节");

            // 计算数据区长度（命令1+方向1+时间戳标记1+时间戳8+实际数据）
            ushort dataAreaLength = (ushort)(2 + 1 + 8 + data.Length);

            // 构建CRC计算数据区
            byte[] crcData = new byte[dataAreaLength];
            crcData[0] = command;
            crcData[1] = direction;
            crcData[2] = 0x01; // 时间戳标记
            Array.Copy(timestampBytes, 0, crcData, 3, 8);
            Array.Copy(data, 0, crcData, 11, data.Length);

            ushort crc = Crc16Modbus(crcData);

            // 构建完整帧
            var frame = new List<byte>
        {
            0xAA, 0x55,                          // 帧头
            (byte)(dataAreaLength & 0xFF),       // 长度低字节
            (byte)(dataAreaLength >> 8),         // 长度高字节
            command,
            direction
        };
            frame.Add(0x01); // 时间戳标记
            frame.AddRange(timestampBytes);
            frame.AddRange(data);
            frame.Add((byte)(crc & 0xFF));
            frame.Add((byte)(crc >> 8));
            frame.Add(FRAME_TAIL);

            return frame.ToArray();
        }

        /// <summary>
        /// CRC16-Modbus 校验
        /// </summary>
        private static ushort Crc16Modbus(byte[] data)
        {
            ushort crc = 0xFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc >>= 1;
                }
            }
            return crc;
        }

        public class FrameReceivedEventArgs : EventArgs
        {
            public ParsedFrame Frame { get; }
            public FrameReceivedEventArgs(ParsedFrame frame) => Frame = frame;
        }
    }

}