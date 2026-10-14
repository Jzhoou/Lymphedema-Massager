# 淋巴水肿按摩仪控制系统 / Lymphedema Massager Control System

> **团队项目与贡献边界**
>
> 本仓库记录的是一个团队工程项目。**本人负责下位机电控程序，并参与了部分机械建模工作**。下位机电控部分包括传感器与执行器驱动、设备通信、数据上报和治疗控制流程；机械建模由团队协作完成，本人参与其中的部分工作。仓库中的 **.NET 8 / WPF 上位机属于完整系统背景，不是本人的个人成果**；保留其代码是为了说明设备端协议的联调对象与整体工程上下文。

## 项目概述

该原型由下位机嵌入式控制程序和上位机应用组成。下位机采集多通道压力与生物电阻抗数据，驱动舵机和步进电机，维护治疗流程状态，并通过 BM53 帧协议与上位机交换命令和状态。上位机提供设备连接、控制、数据展示与记录等团队系统能力。

> [!WARNING]
> **本项目是工程原型，不是经过认证的医疗器械。** 仓库内容仅用于研发、联调与作品展示，不构成诊断、治疗建议或疗效声明。代码中的阈值、校准参数和控制参数需要结合具体硬件进行验证，不应直接用于临床或其他安全关键场景。

## 本人负责的下位机电控程序

### 治疗控制流程：`TherapyStateMachine`

- 管理 `IDLE`、`RUNNING`、`PAUSED`、`EMERGENCY_STOP` 和 `COMPLETED` 状态及合法转换。
- 维护治疗模式、强度和时长配置，计算已用时间、剩余时间与进度。
- 暂停时扣除暂停时长；达到设定时间后转入完成状态。
- 处理正常停止、紧急停止与退出急停，并在治疗期间连接超时后自动停止。
- 状态变化回调将流程变化同步给电机、舵机和数据上报逻辑。

### 压力采集：`MY2901` 与 `PressureSensor`

- `MY2901` 通过串口读取多通道 ADC 数据。
- `PressureSensor` 使用可配置校准点进行分段线性插值，并提供底噪过滤、量程约束、批量转换和校准表校验。
- 转换后的压力值参与舵机反馈控制，并由 `DataReporter` 按变化或周期上报。

### 生物电阻抗测量：`AD5933`

- 通过 I²C 访问 AD5933 的控制、频率、状态和复数数据寄存器。
- 支持初始化、单频校准、频率配置、单次采样以及阻抗/相位计算。
- 主循环以分步状态机完成 20 kHz 与 50 kHz 测量，分别收集有效样本、排序并去除两端值后求均值，避免把完整双频流程集中在一次循环中。
- 校准系数和参考电阻属于具体硬件参数；更换模拟前端、电极或测量链路后应重新标定。

### 执行器控制：`ServoController` 与 `motor_control`

- `ServoController` 基于 PCA9685 PWM 驱动多路舵机，支持单通道/批量 PWM 设置，以及每通道独立的目标压力与 PID 参数。
- 治疗伸出阶段根据多通道压力反馈更新舵机输出；输出限制用于约束 PWM 调节范围。
- `motor_control` 通过独立串口控制两台步进电机，提供伸出、收回、停止、回零、单电机步进和双电机同步/异步步进接口。
- 0x04 电机的到位状态由非阻塞状态机轮询；位置通过阻塞式读取并做短时缓存，当前批量位置上报复用同一位置值。治疗循环根据该到位状态切换运动阶段，并保留超时兜底。

### OLED 显示：`DisplayHelper`

- 封装 SSD1306 OLED 初始化后的显示逻辑。
- 展示阻抗校准开始、进度、成功/失败，以及 20 kHz、50 kHz 测量结果和比值。
- OLED 与 AD5933 共享工程中配置的 I²C 总线，具体引脚与地址见对应头文件。

### 数据汇聚与上报：`DataReporter`

- 将压力、水肿相关阻抗数据、治疗进度、电机状态与电机位置汇聚到 BM53 协议层。
- 按不同数据类型的周期节流发送，并结合压力变化、归零和电机状态变化触发及时上报。
- 治疗开始等关键时刻可强制刷新全量状态，同时维护上报计数。

### BM53 帧协议：`BM53_Protocol`

- 帧解析采用 1024 字节环形接收缓冲区，从连续串口字节流中查找帧头、校验长度、CRC16 与帧尾，再分发到全局或命令级回调。
- 发送侧采用固定容量队列和可配置发送间隔，将组帧与串口写出解耦，并统计队列丢弃、收发字节、已解析帧与 CRC 错误。
- 支持心跳与连接超时判断、握手/设备发现、设备信息，以及 `ACK` / `NAK` / 带结果码响应。
- 控制命令覆盖治疗开始、停止、暂停、继续、急停、退出急停、模式/强度/时长设置，以及步进电机、舵机、回零、位置和系统时间。
- 状态数据覆盖设备信息、单通道或批量压力、水肿相关数据、治疗进度和电机状态；可选时间戳由协议层与 `TimeStamp` 模块协同处理。

## 运行关系

下位机 `setup()` 依次初始化 I²C、步进电机、舵机、压力采集、OLED、AD5933、BM53、治疗状态机、数据上报与时间戳模块，并执行电机回零。`loop()` 持续处理通信、异步电机轮询、治疗状态更新、运动阶段切换、压力反馈、双频阻抗测量与数据上报。

上位机通过 BM53 命令控制下位机；下位机返回确认帧和设备状态。通信断开不会改变本仓库的原型性质，实际安全策略仍需在目标硬件和预期使用场景中完成系统级风险分析与验证。

## 目录结构

```text
Lymphedema-Massager/
├─ README.md                              # 项目介绍、目录与使用说明
├─ 程序/
│  ├─ 下位机/
│  │  └─ main/
│  │     ├─ main.ino                       # Arduino 工程入口
│  │     └─ 其他 .ino / .cpp / .h 文件     # 驱动、通信与流程控制
│  └─ 上位机/
│     └─ measurement-perception-master/   # .NET 8 / WPF 团队工程
└─ 模型/
   └─ 第二版建模sw文件/
      ├─ 总装配体.SLDASM                  # 整机总装配体
      ├─ 按摩环内装配体.SLDASM
      ├─ 舵机按摩头装配体.SLDASM
      ├─ 其他 .SLDASM / .SLDPRT 文件      # 子装配体与配套零件
      └─ 压力/                           # 压力传感器模型与 STP 文件
```

- [程序目录](程序/)：上位机与下位机源码。
- [建模目录](模型/)：SolidWorks 装配体、零件和 STP 模型，保留原文件名与子目录结构。

## 构建说明

### 下位机

下位机代码采用 Arduino 风格工程结构，目标硬件相关接口包括多路硬件串口、I²C、ADC 采集模块、PCA9685 舵机驱动、SSD1306 OLED 和 AD5933。构建前需要：

1. 安装与目标 BMduino/Arduino 兼容控制板匹配的开发板支持包和工具链。
2. 安装源码引用的库：`Wire`、`Adafruit GFX Library`、`Adafruit SSD1306`、`Adafruit PWM Servo Driver Library`（及其依赖 `Adafruit BusIO`）。
3. 在 IDE 或等价 CLI 环境中打开 [`程序/下位机/main/main.ino`](程序/下位机/main/main.ino)，选择实际控制板与端口后编译。
4. 烧录前复核 `config.h`、AD5933 校准系数、串口/I²C 引脚、限位开关、方向、行程及压力控制参数；这些值与具体样机绑定。

仓库没有提供可复现的锁定版开发板包或一键式固件构建脚本，因此不同本地工具链可能需要额外的板卡配置。

### 上位机（系统背景）

要求 Windows 与 .NET 8 SDK。在仓库根目录运行：

```powershell
dotnet restore "程序/上位机/measurement-perception-master/Upcomputer.App/Upcomputer.App.csproj"
dotnet build "程序/上位机/measurement-perception-master/Upcomputer.App/Upcomputer.App.csproj" --nologo --verbosity:minimal
```

该 WPF 工程用于说明团队系统和联调环境，不代表本人的个人交付范围。

## 机械结构与建模

[`模型/第二版建模sw文件/`](模型/第二版建模sw文件/) 保存第二版样机的机械设计资料，覆盖整机装配、按摩执行机构、传动与支撑结构，以及压力传感器相关部件。建模资料由团队协作完成，**本人参与了部分机械建模工作**。

### 主要结构模块

以下按现有装配体与零件文件介绍模型内容：

| 结构模块 | 代表模型 | 内容与用途 |
| --- | --- | --- |
| 整机与支撑框架 | `总装配体.SLDASM`、`工作台.SLDPRT`、多种长度的铝型材 | 展示整机部件布局与装配关系，提供工作台和框架的结构参考 |
| 按摩环与齿轮部件 | `按摩环内装配体.SLDASM`、`按摩圈3.0.SLDPRT`、主动/被动齿轮、齿轮座盖 | 包含环形按摩机构及配套齿轮、固定件，可结合装配体查看部件之间的配合 |
| 舵机与按摩头 | `舵机按摩头装配体.SLDASM`、`按摩头2.0.SLDPRT`、按摩头打印版凹/凸件、连接件 | 包含舵机安装、按摩头及其连接结构，并保留按摩头的不同建模版本 |
| 丝杆传动与导向支撑 | 丝杆、丝杆电机、丝杆法兰、光杆、联轴器、轴承及末端固定件 | 提供执行机构直线运动所需的传动、连接、导向与支撑部件模型 |
| 外壳与收纳 | `外壳装配体.SLDASM`、`外壳1.SLDPRT`、`外壳2.SLDPRT`、`收纳盒.SLDASM` | 包含外部壳体与收纳部件，便于查看机械部件的包覆和安装空间 |
| 压力传感器部件 | `压力/压力传感器.SLDASM`、上盖、下盖、柱塞及 `.stp` 文件 | 单独整理传感器相关装配和零件，提供 SolidWorks 原生模型与部分交换格式文件 |

目录中还包含 `DS1202三维图.SLDPRT`、`ZDT_X42_L40Motor.SLDPRT` 等配套部件模型，可结合整机装配查看安装位置与空间关系。

### 文件格式与查看方式

- `.SLDASM` 为 SolidWorks 装配体，`.SLDPRT` 为零件；部分压力部件另有 `.stp` 交换格式文件。
- 建议先打开 [`总装配体.SLDASM`](模型/第二版建模sw文件/总装配体.SLDASM) 查看整机，再查看按摩环、舵机按摩头和压力传感器等子装配体。
- 请下载完整模型目录并保留原文件名及子目录，以便装配体查找配套零件。同类零件保留了不同版本，具体采用关系以装配体实际引用为准。

本目录共归档 59 个模型文件，上传时排除了 `~$` 开头的 SolidWorks 临时锁文件。

## English summary

This repository contains a **team-built engineering prototype** of a lymphedema massager control system. **I was responsible for the embedded controller firmware and also contributed to part of the mechanical CAD modeling.** The firmware work covers sensor and actuator drivers, BM53 device communication, telemetry, and therapy-control flow. The mechanical models were developed collaboratively by the team. The .NET 8/WPF desktop application is included only as system and integration context and is **not claimed as my individual work**.

The firmware implements a therapy state machine; calibrated multi-channel pressure acquisition; I²C-based AD5933 impedance measurement; servo pressure feedback and dual stepper-motor control; SSD1306 OLED status views; scheduled data reporting; and a BM53 framed protocol with an RX ring buffer, TX queue, heartbeat tracking, ACK/NAK handling, and device, pressure, edema-related, progress, and motor-status payloads. Motor `0x04` arrival status is polled by a non-blocking state machine, while position reads are blocking and briefly cached; the current batch position report reuses that same position value for both motor fields.

This is an engineering prototype, **not a certified medical device**. No clinical efficacy, safety performance, or measurement-accuracy claim is made. Hardware-specific calibration and full system validation are required before any safety-critical use.
