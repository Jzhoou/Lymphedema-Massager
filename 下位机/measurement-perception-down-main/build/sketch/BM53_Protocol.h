#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\BM53_Protocol.h"
// BM53_Protocol.h
#ifndef BM53_PROTOCOL_H
#define BM53_PROTOCOL_H

#include <Arduino.h>
#include "TimeStamp.h"  // ★ 时间戳模块

// ============================================================
// 协议常量
// ============================================================
#define BM53_FRAME_H1      0xAA
#define BM53_FRAME_H2      0x55
#define BM53_FRAME_TAIL    0x0D
#define BM53_DIR_UP2DEV    0x00
#define BM53_DIR_DEV2UP    0x01

// 命令码
#define BM53_CMD_HEARTBEAT     0x01
#define BM53_CMD_HANDSHAKE     0x02
#define BM53_CMD_DEV_INFO      0x03
#define BM53_CMD_DISCOVERY_REQ 0x04
#define BM53_CMD_DISCOVERY_RES 0x05

#define BM53_CMD_START_THERAPY  0x10
#define BM53_CMD_STOP_THERAPY   0x11
#define BM53_CMD_EME_STOP       0x12
#define BM53_CMD_SET_MODE       0x13
#define BM53_CMD_SET_INTENSITY  0x14
#define BM53_CMD_SET_DURATION   0x15
#define BM53_CMD_PAUSE_THERAPY   0x16   // 暂停治疗
#define BM53_CMD_RESUME_THERAPY  0x17   // 继续治疗
#define BM53_CMD_EXIT_EME_STOP   0x18   // 退出急停状态

#define BM53_CMD_STEPPER_CTRL   0x30
#define BM53_CMD_SERVO_CTRL     0x31
#define BM53_CMD_SERVO_HOME     0x32
#define BM53_CMD_MOTOR_HOME      0x33   // 电机回零
#define BM53_CMD_MOTOR_STEP      0x34   // 电机步进（单步移动）
#define BM53_CMD_MOTOR_POSITION  0x35   // 读取电机当前位置
#define BM53_CMD_SET_TIME        0x36   // ★ 设置系统时间（上位机发NTP时间）

#define BM53_CMD_PRESSURE       0x50
#define BM53_CMD_EDEMA          0x51
#define BM53_CMD_MOTOR_STATUS   0x52
#define BM53_CMD_PROGRESS       0x53

#define BM53_CMD_ACK            0x80
#define BM53_CMD_NAK            0x81


// 最大帧长度
#define BM53_MAX_FRAME_SIZE 256

// ============================================================
// 发送队列配置
// ============================================================
#define BM53_SEND_QUEUE_SIZE 32        // 发送队列最大容量
#define BM53_SEND_INTERVAL_MS 50       // 发送间隔（毫秒）

// 队列项结构
struct SendQueueItem {
    byte frame[BM53_MAX_FRAME_SIZE];   // 完整帧数据
    int frameLen;                      // 帧长度
    bool valid;                        // 是否有效
};

// ============================================================
// 回调函数类型定义
// ============================================================
typedef void (*CommandCallback)(byte cmd, byte* data, uint16_t dataLen);

// ============================================================
// BM53_Protocol 类
// ============================================================
class BM53_Protocol {
public:
    // 构造函数
    BM53_Protocol(HardwareSerial& serial = Serial2);
    
    // 初始化和主循环
    void begin(unsigned long baudRate = 460800);
    void loop();
    
    // 注册回调
    void onCommand(CommandCallback callback);
    void onCommand(byte cmd, CommandCallback callback);
    
    // 发送帧（所有帧统一写串口，时间戳由WiFi模块决定）
    bool sendFrame(byte cmd, byte* data = nullptr, uint16_t dataLen = 0);
    void sendAck();
    void sendNak();
    void sendResponse(byte cmd, byte result, byte error = 0);
    
    // 便捷发送函数
    void sendHeartbeat();
    void sendDeviceInfo(const char* devName, const char* firmVer);
    void sendPressureData(byte sensorId, float currentValue, float maxValue, 
                          float threshold, byte alertFlag);
    void sendEdemaData(byte channelId, float impedance, float edemaPercent, byte alert);
    void sendProgressData(byte percent, uint16_t elapsedSeconds, uint16_t remainingSeconds);
    void sendMotorStatus(byte stepper1State, byte stepper2State,
                         uint16_t stepper1Speed, uint16_t stepper2Speed,
                         byte* servoAngles, byte servoCount);
    
    // 发送队列控制
    void setSendInterval(unsigned long intervalMs);
    void flushSendQueue();
    int getQueueCount();
    void clearSendQueue();
    
    // 获取统计信息
    unsigned long getBytesReceived() const { return _totalBytesReceived; }
    unsigned long getBytesSent() const { return _totalBytesSent; }
    unsigned long getFramesParsed() const { return _totalFramesParsed; }
    unsigned long getFramesSent() const { return _totalFramesSent; }
    unsigned long getCrcErrors() const { return _totalCrcErrors; }
    unsigned long getLastHeartbeatTime() const { return _lastHeartbeatTime; }
    unsigned long getQueueDropped() const { return _queueDropped; }

    // 批量发送函数
    void sendPressureDataBatch(byte sensorCount, const byte* sensorIds, 
                           const float* currentValues, const float* maxValues, 
                           const float* thresholds, const byte* alertFlags);
    
    // 连接状态
    bool isConnected() const;
    void setHeartbeatTimeout(unsigned long timeoutMs) { _heartbeatTimeout = timeoutMs; }
    
    // 调试开关
    void setDebug(bool enable) { _debugEnable = enable; }
    void setDebugStream(Stream* stream) { _debugStream = stream; }
    
    // CRC计算（静态方法，供外部使用）
    static uint16_t crc16(byte* data, int len);

    // ★ 时间戳控制
    void enableTimestamp(bool enable) { _timestampEnabled = enable; }
    bool isTimestampEnabled() const { return _timestampEnabled; }

private:
    // 串口引用
    HardwareSerial& _serial;
    Stream* _debugStream;
    
    // 环形缓冲区
    static const int RING_SIZE = 1024;
    byte _ringBuffer[RING_SIZE];
    volatile int _ringHead;
    volatile int _ringTail;
    
    // 发送队列
    SendQueueItem _sendQueue[BM53_SEND_QUEUE_SIZE];
    volatile int _queueHead;
    volatile int _queueTail;
    unsigned long _lastSendTime;
    unsigned long _sendInterval;
    unsigned long _queueDropped;
    
    // 统计信息
    unsigned long _totalBytesReceived;
    unsigned long _totalBytesSent;
    unsigned long _totalFramesParsed;
    unsigned long _totalFramesSent;
    unsigned long _totalCrcErrors;
    unsigned long _lastHeartbeatTime;
    unsigned long _heartbeatTimeout;
    
    // 配置
    bool _debugEnable;
    bool _timestampEnabled;

    // 回调
    CommandCallback _globalCallback;
    CommandCallback _cmdCallbacks[256];
    
    // 内部方法
    void _readToRingBuffer();
    void _extractFrame();
    void _parseFrame(byte* frame, int len);
    bool _sendFrameInternal(byte cmd, byte* data, uint16_t dataLen);
    void _processSendQueue();
    bool _queueFrame(byte* frame, int len);
    void _printHex(byte b);
    void _printHexArray(byte* data, int len, int maxPrint = 32);
    void _printTimestamp();
    const char* _getCmdName(byte cmd);
    // ★ 解析接收帧中的时间戳，返回跳过的字节数
    byte _parseTimestamp(const byte* buf, uint16_t dataLen);
};

#endif // BM53_PROTOCOL_H