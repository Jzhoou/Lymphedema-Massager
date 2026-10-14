using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Upcomputer.Core.Models;


namespace Upcomputer.Communication.Protocol
{
    /// <summary>
    /// 命令构建器 - 将请求对象转换为协议数据格式
    /// </summary>
    public static class CommandBuilder
    {
        /// <summary>
        /// 构建启动治疗命令数据
        /// </summary>
        public static byte[] BuildStartTherapy(StartTherapyRequest request)
        {
            return new byte[]
            {
            (byte)request.Mode,
            request.Intensity,
            request.Duration
            };
        }

        /// <summary>
        /// 构建设置模式命令数据
        /// </summary>
        public static byte[] BuildSetMode(SetModeRequest request)
        {
            return new byte[] { (byte)request.Mode };
        }

        /// <summary>
        /// 构建设置强度命令数据
        /// </summary>
        public static byte[] BuildSetIntensity(SetIntensityRequest request)
        {
            return new byte[] { request.Intensity };
        }

        /// <summary>
        /// 构建设置时长命令数据
        /// </summary>
        public static byte[] BuildSetDuration(SetDurationRequest request)
        {
            return new byte[] { request.Duration };
        }

        /// <summary>
        /// 构建步进电机控制命令数据
        /// 格式: [电机ID(1)] [转速低字节] [转速高字节] [方向(1)]
        /// </summary>
        public static byte[] BuildStepperControl(StepperControlRequest request)
        {
            return new byte[]
            {
            request.MotorId,
            (byte)(request.Speed & 0xFF),       // 转速低字节
            (byte)((request.Speed >> 8) & 0xFF), // 转速高字节
            request.Direction
            };
        }

        /// <summary>
        /// 构建舵机控制命令数据
        /// 格式: [舵机ID(1)] [角度(1)]
        /// </summary>
        public static byte[] BuildServoControl(ServoControlRequest request)
        {
            return new byte[] { request.ServoId, request.Angle };
        }

        /// <summary>
        /// 构建设备发现查询数据
        /// </summary>
        public static byte[] BuildDeviceDiscovery()
        {
            return Encoding.UTF8.GetBytes("UPCOMPUTER_DISCOVER");
        }

        /// <summary>
        /// 构建电机步进命令数据
        /// 数据7字节:
        /// [0] 方向: 0x00=正转, 0x01=反转
        /// [1-4] 步数: 4字节无符号整型（高字节在前）
        /// [5-6] 速度: 2字节无符号整型（高字节在前，单位RPM）
        /// </summary>
        public static byte[] BuildMotorStep(MotorStepRequest request)
        {
            return new byte[]
            {
                request.Direction,
                (byte)((request.Steps >> 24) & 0xFF),
                (byte)((request.Steps >> 16) & 0xFF),
                (byte)((request.Steps >> 8) & 0xFF),
                (byte)(request.Steps & 0xFF),
                (byte)((request.SpeedRpm >> 8) & 0xFF),
                (byte)(request.SpeedRpm & 0xFF)
            };
        }

        /// <summary>
        /// 构建设置时间命令数据
        /// 6字节小端序 UTC+8 Unix 秒时间戳
        /// </summary>
        public static byte[] BuildSetTime()
        {
            // 28800秒 = 8小时 × 3600秒
            long timestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 28800;
            byte[] result = new byte[6];
            result[0] = (byte)(timestampSeconds & 0xFF);
            result[1] = (byte)((timestampSeconds >> 8) & 0xFF);
            result[2] = (byte)((timestampSeconds >> 16) & 0xFF);
            result[3] = (byte)((timestampSeconds >> 24) & 0xFF);
            result[4] = (byte)((timestampSeconds >> 32) & 0xFF);
            result[5] = (byte)((timestampSeconds >> 40) & 0xFF);
            return result; // 6字节小端序
        }
    }
}
