using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Core.Models;

namespace Upcomputer.Communication.Protocol
{
    /// <summary>
    /// 数据解析器 - 将协议数据解析为对象
    /// </summary>
    public static class DataParser
    {
        /// <summary>
        /// 解析压力数据
        /// 格式: [数量(1)] [传感器ID(1)] [当前值(4)] [最大值(4)] [阈值(4)] [告警标志(1)] ...
        /// </summary>
        public static PressureDataReport ParsePressureData(byte[] data)
        {
            const int recordLength = 14;

            if (data.Length < recordLength)
                throw new ArgumentException("压力数据长度不足");

            var report = new PressureDataReport();
            int offset = 0;
            int sensorCount;

            if (data.Length >= 1 && data.Length == 1 + data[0] * recordLength)
            {
                sensorCount = data[0];
                offset = 1;
            }
            else if (data.Length % recordLength == 0)
            {
                sensorCount = data.Length / recordLength;
            }
            else
            {
                throw new ArgumentException("压力数据格式不正确");
            }

            for (var i = 0; i < sensorCount; i++)
            {
                if (offset + recordLength > data.Length)
                {
                    throw new ArgumentException("压力数据长度不足");
                }

                report.Items.Add(new PressureDataItem
                {
                    SensorId = data[offset],
                    CurrentValue = BitConverter.ToSingle(data, offset + 1),
                    MaxValue = BitConverter.ToSingle(data, offset + 5),
                    Threshold = BitConverter.ToSingle(data, offset + 9),
                    AlertFlag = data[offset + 13]
                });

                offset += recordLength;
            }

            return report;
        }

        /// <summary>
        /// 解析水肿/阻抗数据
        /// 格式: [传感器ID(1)] [阻抗值(4)] [水肿百分比(4)] [告警标志(1)]
        /// </summary>
        public static EdemaDataReport ParseEdemaData(byte[] data)
        {
            if (data.Length < 10)
                throw new ArgumentException("水肿数据长度不足");

            return new EdemaDataReport
            {
                SensorId = data[0],
                Impedance = BitConverter.ToSingle(data, 1),
                EdemaPercent = BitConverter.ToSingle(data, 5),
                AlertFlag = data[9]
            };
        }

        /// <summary>
        /// 解析电机状态数据
        /// 格式: [步进1状态(1)] [步进2状态(1)] [步进1转速(2)] [步进2转速(2)] [舵机1-6角度(6)]
        /// </summary>
        public static MotorStatusReport ParseMotorStatus(byte[] data)
        {
            if (data.Length < 12)
                throw new ArgumentException("电机状态数据长度不足");

            var report = new MotorStatusReport
            {
                Stepper1State = data[0],
                Stepper2State = data[1],
                Stepper1Speed = (ushort)(data[2] | (data[3] << 8)),
                Stepper2Speed = (ushort)(data[4] | (data[5] << 8))
            };

            // 舵机角度
            for (int i = 0; i < 6 && i + 6 < data.Length; i++)
            {
                report.ServoAngles[i] = data[6 + i];
            }

            return report;
        }

        /// <summary>
        /// 解析电机位置数据
        /// 格式: [2号电机位置(4)] [4号电机位置(4)]，均为大端序 int32
        /// </summary>
        public static MotorPositionResponse ParseMotorPosition(byte[] data)
        {
            if (data.Length < 8)
                throw new ArgumentException("电机位置数据长度不足（需要8字节）");

            int motor2Value = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
            int motor4Value = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];

            return new MotorPositionResponse
            {
                Motor2EncoderValue = motor2Value,
                Motor4EncoderValue = motor4Value
            };
        }

        /// <summary>
        /// 解析治疗进度数据
        /// 格式: [进度(1)] [已过时间(2)] [剩余时间(2)]
        /// </summary>
        public static TherapyProgressReport ParseTherapyProgress(byte[] data)
        {
            if (data.Length < 5)
                throw new ArgumentException("治疗进度数据长度不足");

            return new TherapyProgressReport
            {
                Progress = data[0],
                ElapsedTime = (ushort)(data[1] | (data[2] << 8)),
                RemainingTime = (ushort)(data[3] | (data[4] << 8))
            };
        }

        /// <summary>
        /// 解析通用响应
        /// 格式: [结果(1)] [错误码(1)]
        /// </summary>
        public static GeneralResponse ParseGeneralResponse(byte[] data)
        {
            if (data.Length < 2)
                return new GeneralResponse { Result = 0, ErrorCode = 0 };

            return new GeneralResponse
            {
                Result = data[0],
                ErrorCode = data.Length > 1 ? data[1] : (byte)0
            };
        }

        /// <summary>
        /// 解析设备信息响应
        /// 格式: [设备名(15)] [固件版本(15)] [状态(1)]
        /// </summary>
        public static DeviceInfoResponse ParseDeviceInfo(byte[] data)
        {
            var response = new DeviceInfoResponse();

            if (data.Length >= 15)
            {
                response.DeviceName = Encoding.UTF8.GetString(data, 0, 15).TrimEnd('\0');
            }

            if (data.Length >= 30)
            {
                response.FirmwareVersion = Encoding.UTF8.GetString(data, 15, 15).TrimEnd('\0');
            }

            if (data.Length >= 31)
            {
                response.DeviceStatus = data[30];
            }

            return response;
        }

        /// <summary>
        /// 解析设备通告数据
        /// 格式: [IP(4)] [端口(2)] [设备名(15)] [版本(15)] [状态(1)]
        /// </summary>
        public static DeviceAnnounceData ParseDeviceAnnounce(byte[] data)
        {
            var announce = new DeviceAnnounceData();

            int offset = 0;

            // IP地址
            if (data.Length >= 4)
            {
                Array.Copy(data, offset, announce.IpAddress, 0, 4);
                offset += 4;
            }

            // 端口
            if (data.Length >= offset + 2)
            {
                announce.TcpPort = (ushort)(data[offset] | (data[offset + 1] << 8));
                offset += 2;
            }

            // 设备名称
            if (data.Length >= offset + 15)
            {
                announce.DeviceName = Encoding.UTF8.GetString(data, offset, 15).TrimEnd('\0');
                offset += 15;
            }

            // 版本
            if (data.Length >= offset + 15)
            {
                announce.Version = Encoding.UTF8.GetString(data, offset, 15).TrimEnd('\0');
                offset += 15;
            }

            // 状态
            if (data.Length >= offset + 1)
            {
                announce.Status = data[offset];
            }

            return announce;
        }
    }
}
