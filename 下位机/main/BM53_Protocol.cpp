#include "BM53_Protocol.h"
#include "TimeStamp.h"  // ★ 时间戳模块

// ============================================================
// 构造函数
// ============================================================
BM53_Protocol::BM53_Protocol(HardwareSerial& serial)
    : _serial(serial)
    , _debugStream(&Serial)
    , _ringHead(0)
    , _ringTail(0)
    , _queueHead(0)
    , _queueTail(0)
    , _lastSendTime(0)
    , _sendInterval(BM53_SEND_INTERVAL_MS)
    , _queueDropped(0)
    , _totalBytesReceived(0)
    , _totalBytesSent(0)
    , _totalFramesParsed(0)
    , _totalFramesSent(0)
    , _totalCrcErrors(0)
    , _lastHeartbeatTime(0)
    , _heartbeatTimeout(8000)
    , _debugEnable(false)
    , _timestampEnabled(true)
    , _globalCallback(nullptr)
{
    memset(_cmdCallbacks, 0, sizeof(_cmdCallbacks));
    for (int i = 0; i < BM53_SEND_QUEUE_SIZE; i++) {
        _sendQueue[i].valid = false;
    }
}

// ============================================================
// 初始化
// ============================================================
void BM53_Protocol::begin(unsigned long baudRate) {
    _serial.begin(baudRate);
    _ringHead = _ringTail = 0;
    _queueHead = _queueTail = 0;
    _lastHeartbeatTime = millis();
    _lastSendTime = millis();

    if (_debugEnable && _debugStream) {
        _debugStream->println(F("[BM53] 协议库初始化完成"));
        _debugStream->print(F("[BM53] 波特率: "));
        _debugStream->println(baudRate);
        _debugStream->print(F("[BM53] 发送间隔: "));
        _debugStream->print(_sendInterval);
        _debugStream->println(F("ms"));
    }
}

// ============================================================
// 主循环
// ============================================================
void BM53_Protocol::loop() {
    _readToRingBuffer();
    _extractFrame();
    _processSendQueue();
}

// ============================================================
// 发送队列处理
// ============================================================
void BM53_Protocol::_processSendQueue() {
    unsigned long now = millis();

    if (now - _lastSendTime < _sendInterval) return;
    if (_queueHead == _queueTail) return;

    SendQueueItem& item = _sendQueue[_queueHead];
    if (!item.valid) {
        _queueHead = (_queueHead + 1) % BM53_SEND_QUEUE_SIZE;
        return;
    }

    _serial.write(item.frame, item.frameLen);
    _totalBytesSent += item.frameLen;
    _totalFramesSent++;

    item.valid = false;
    _queueHead = (_queueHead + 1) % BM53_SEND_QUEUE_SIZE;
    _lastSendTime = now;
}

// ============================================================
// 将帧加入队列
// ============================================================
bool BM53_Protocol::_queueFrame(byte* frame, int len) {
    int nextTail = (_queueTail + 1) % BM53_SEND_QUEUE_SIZE;

    if (nextTail == _queueHead) {
        _queueDropped++;
        if (_debugEnable && _debugStream) {
            _debugStream->println(F("[BM53] 警告: 发送队列满，丢弃数据"));
        }
        _sendQueue[_queueHead].valid = false;
        _queueHead = (_queueHead + 1) % BM53_SEND_QUEUE_SIZE;
    }

    SendQueueItem& item = _sendQueue[_queueTail];
    memcpy(item.frame, frame, len);
    item.frameLen = len;
    item.valid = true;
    _queueTail = nextTail;

    return true;
}

// ============================================================
// 设置发送间隔
// ============================================================
void BM53_Protocol::setSendInterval(unsigned long intervalMs) {
    _sendInterval = intervalMs;
}

// ============================================================
// 立即发送队列中所有数据
// ============================================================
void BM53_Protocol::flushSendQueue() {
    while (_queueHead != _queueTail) {
        SendQueueItem& item = _sendQueue[_queueHead];
        if (item.valid) {
            _serial.write(item.frame, item.frameLen);
            _totalBytesSent += item.frameLen;
            _totalFramesSent++;
            item.valid = false;
        }
        _queueHead = (_queueHead + 1) % BM53_SEND_QUEUE_SIZE;
    }
    _lastSendTime = millis();
}

// ============================================================
// 获取队列中的数据包数量
// ============================================================
int BM53_Protocol::getQueueCount() {
    return (_queueTail - _queueHead + BM53_SEND_QUEUE_SIZE) % BM53_SEND_QUEUE_SIZE;
}

// ============================================================
// 清空发送队列
// ============================================================
void BM53_Protocol::clearSendQueue() {
    while (_queueHead != _queueTail) {
        _sendQueue[_queueHead].valid = false;
        _queueHead = (_queueHead + 1) % BM53_SEND_QUEUE_SIZE;
    }
    _queueDropped = 0;
}

// ============================================================
// 注册回调
// ============================================================
void BM53_Protocol::onCommand(CommandCallback callback) {
    _globalCallback = callback;
}

void BM53_Protocol::onCommand(byte cmd, CommandCallback callback) {
    if (cmd < 256) {
        _cmdCallbacks[cmd] = callback;
    }
}

// ============================================================
// 连接状态
// ============================================================
bool BM53_Protocol::isConnected() const {
    return (millis() - _lastHeartbeatTime) < _heartbeatTimeout;
}

// ============================================================
// 读取到环形缓冲区
// ============================================================
void BM53_Protocol::_readToRingBuffer() {
    int available = _serial.available();
    if (available <= 0) return;

    _totalBytesReceived += available;

    int space = (RING_SIZE + _ringTail - _ringHead - 1) % RING_SIZE;
    if (space < available) {
        _ringTail = (_ringTail + available - space) % RING_SIZE;
    }

    int toRead = min(available, space);

    if (_ringHead + toRead <= RING_SIZE) {
        _serial.readBytes(_ringBuffer + _ringHead, toRead);
    } else {
        int firstPart = RING_SIZE - _ringHead;
        _serial.readBytes(_ringBuffer + _ringHead, firstPart);
        _serial.readBytes(_ringBuffer, toRead - firstPart);
    }

    _ringHead = (_ringHead + toRead) % RING_SIZE;
}

// ============================================================
// 提取帧
// ============================================================
void BM53_Protocol::_extractFrame() {
    static byte frameBuffer[BM53_MAX_FRAME_SIZE];
    static int frameIdx = 0;
    static uint16_t expectedLen = 0;

    while (_ringTail != _ringHead) {
        byte b = _ringBuffer[_ringTail];
        _ringTail = (_ringTail + 1) % RING_SIZE;

        if (frameIdx == 0) {
            if (b == BM53_FRAME_H1) {
                frameBuffer[frameIdx++] = b;
            }
            continue;
        }

        if (frameIdx == 1) {
            if (b == BM53_FRAME_H2) {
                frameBuffer[frameIdx++] = b;
            } else {
                frameIdx = 0;
            }
            continue;
        }

        if (frameIdx < BM53_MAX_FRAME_SIZE) {
            frameBuffer[frameIdx++] = b;
        } else {
            frameIdx = 0;
            continue;
        }

        if (frameIdx == 4) {
            expectedLen = frameBuffer[2] | (frameBuffer[3] << 8);
            expectedLen = 2 + 2 + expectedLen + 2 + 1;

            if (expectedLen > BM53_MAX_FRAME_SIZE) {
                frameIdx = 0;
                continue;
            }
        }

        if (frameIdx >= 7 && frameIdx == expectedLen) {
            if (frameBuffer[frameIdx - 1] == BM53_FRAME_TAIL &&
                frameBuffer[0] == BM53_FRAME_H1 &&
                frameBuffer[1] == BM53_FRAME_H2) {

                _parseFrame(frameBuffer, frameIdx);
            }
            frameIdx = 0;
            break;
        }
    }
}

// ============================================================
// ★ 解析接收帧中的时间戳，返回时间戳占用的字节数
// ============================================================
byte BM53_Protocol::_parseTimestamp(const byte* buf, uint16_t dataLen) {
    // dataLen 包含 dir(1) 后面的数据部分
    // 接收帧格式: H1(1) H2(1) dataLen(2) cmd(1) dir(1) [timestamp?] payload... CRC(2) tail(1)
    // 数据区 = cmd + dir + payload，dataLen = 2 + payloadLen [+ timestampLen]
    //
    // 如果 dataLen > 2，说明有payload，前1字节可能是时间戳flag
    // 如果前1字节是 0x01 或 0x00，且后面有足够数据，就是时间戳
    
    if (dataLen <= 2) return 0;  // 无payload，无时间戳
    
    // 时间戳在 dir(1) 之后的第一个字节
    byte flag = buf[7];  // buf[6]是dir, buf[7]是第一个数据字节
    
    if (flag == TIMESTAMP_FLAG_ABS && dataLen >= 1 + 8 + 1) {
        // 绝对时间：flag(1) + timestamp(8) + 至少1字节数据
        return TIMESTAMP_LEN_ABS;  // 9
    } else if (flag == TIMESTAMP_FLAG_REL && dataLen >= 1 + 4 + 1) {
        // 相对时间：flag(1) + timestamp(4) + 至少1字节数据
        return TIMESTAMP_LEN_REL;  // 5
    }
    
    return 0;  // 不是时间戳，不是标准数据帧，不跳过
}

// ============================================================
// 解析帧
// ============================================================
void BM53_Protocol::_parseFrame(byte* buf, int len) {
    if (buf[0] != BM53_FRAME_H1 || buf[1] != BM53_FRAME_H2 ||
        buf[len-1] != BM53_FRAME_TAIL) {
        return;
    }

    uint16_t dataLen = buf[2] | (buf[3] << 8);
    byte cmd = buf[4];
    byte dir = buf[5];

    // CRC校验
    int crcPos = len - 3;
    uint16_t rcvCrc = buf[crcPos] | (buf[crcPos + 1] << 8);
    uint16_t calCrc = crc16(buf + 4, dataLen);

    if (_debugEnable && _debugStream) {
        _debugStream->print(F("<< [解析] "));
        _debugStream->print(_getCmdName(cmd));
        _debugStream->print(F(" CRC:"));
        _debugStream->println(rcvCrc == calCrc ? F("正确") : F("错误"));
    }

    if (rcvCrc != calCrc) {
        _totalCrcErrors++;
        return;
    }

    _totalFramesParsed++;

    // 心跳特殊处理
    if (cmd == BM53_CMD_HEARTBEAT) {
        _lastHeartbeatTime = millis();
        sendHeartbeat();
    }

    // ★ 调用回调：跳过时间戳，只传实际数据
    byte* dataPtr = nullptr;
    uint16_t payloadLen = 0;

    if (dataLen > 2) {
        byte tsLen = _parseTimestamp(buf, dataLen);
        // 数据起始位置: buf[6] = dir, buf[7] = 第一个数据字节
        dataPtr = buf + 6 + tsLen;
        payloadLen = dataLen - 2 - tsLen;  // 减去 dir(1) + cmd(1) 已在dataLen计算中的dir
    }

    if (_cmdCallbacks[cmd]) {
        _cmdCallbacks[cmd](cmd, dataPtr, payloadLen);
    }

    if (_globalCallback) {
        _globalCallback(cmd, dataPtr, payloadLen);
    }
}

// ============================================================
// ★ 内部发送函数：统一写串口，所有有数据的帧都加时间戳
// ============================================================
bool BM53_Protocol::_sendFrameInternal(byte cmd, byte* data, uint16_t dataLen) {
    byte frame[BM53_MAX_FRAME_SIZE];
    int idx = 0;

    frame[idx++] = BM53_FRAME_H1;
    frame[idx++] = BM53_FRAME_H2;

    // ★ 时间戳插入：所有有数据的帧统一加时间戳
    uint8_t tsLen = 0;
    byte tsBuf[9];
    if (_timestampEnabled && data != nullptr && dataLen > 0) {
        tsLen = systemTime.getTimestampBytes(tsBuf);
    }

    uint16_t dataArea = 2 + tsLen + dataLen;
    frame[idx++] = dataArea & 0xFF;
    frame[idx++] = dataArea >> 8;

    frame[idx++] = cmd;
    frame[idx++] = BM53_DIR_DEV2UP;

    // ★ 插入时间戳
    if (tsLen > 0) {
        memcpy(frame + idx, tsBuf, tsLen);
        idx += tsLen;
    }

    if (data != nullptr && dataLen > 0) {
        memcpy(frame + idx, data, dataLen);
        idx += dataLen;
    }

    uint16_t crc = crc16(frame + 4, dataArea);
    frame[idx++] = crc & 0xFF;
    frame[idx++] = crc >> 8;
    frame[idx++] = BM53_FRAME_TAIL;

    // 加入发送队列
    return _queueFrame(frame, idx);
}

// ============================================================
// 公共发送接口
// ============================================================
bool BM53_Protocol::sendFrame(byte cmd, byte* data, uint16_t dataLen) {
    return _sendFrameInternal(cmd, data, dataLen);
}

void BM53_Protocol::sendAck() {
    _sendFrameInternal(BM53_CMD_ACK, nullptr, 0);
}

void BM53_Protocol::sendNak() {
    _sendFrameInternal(BM53_CMD_NAK, nullptr, 0);
}

void BM53_Protocol::sendResponse(byte cmd, byte result, byte error) {
    byte buf[2] = {result, error};
    _sendFrameInternal(cmd, buf, 2);
}

void BM53_Protocol::sendHeartbeat() {
    _sendFrameInternal(BM53_CMD_HEARTBEAT, nullptr, 0);
}

void BM53_Protocol::sendDeviceInfo(const char* devName, const char* firmVer) {
    byte buf[30];
    memset(buf, 0, 30);
    strncpy((char*)buf, devName, 15);
    strncpy((char*)buf + 15, firmVer, 15);
    _sendFrameInternal(BM53_CMD_DEV_INFO, buf, 30);
}

void BM53_Protocol::sendPressureData(byte sensorId, float currentValue,
                                      float maxValue, float threshold, byte alertFlag) {
    byte buf[14];
    buf[0] = sensorId;
    memcpy(buf + 1, &currentValue, 4);
    memcpy(buf + 5, &maxValue, 4);
    memcpy(buf + 9, &threshold, 4);
    buf[13] = alertFlag;
    _sendFrameInternal(BM53_CMD_PRESSURE, buf, 14);
}

void BM53_Protocol::sendEdemaData(byte channelId, float impedance,
                                   float edemaPercent, byte alert) {
    byte buf[10];
    buf[0] = channelId;
    memcpy(buf + 1, &impedance, 4);
    memcpy(buf + 5, &edemaPercent, 4);
    buf[9] = alert;
    _sendFrameInternal(BM53_CMD_EDEMA, buf, 10);
}

void BM53_Protocol::sendProgressData(byte percent, uint16_t elapsedSeconds,
                                      uint16_t remainingSeconds) {
    byte buf[5] = {
        percent,
        (byte)(elapsedSeconds & 0xFF),
        (byte)(elapsedSeconds >> 8),
        (byte)(remainingSeconds & 0xFF),
        (byte)(remainingSeconds >> 8)
    };
    _sendFrameInternal(BM53_CMD_PROGRESS, buf, 5);
}

void BM53_Protocol::sendMotorStatus(byte stepper1State, byte stepper2State,
                                     uint16_t stepper1Speed, uint16_t stepper2Speed,
                                     byte* servoAngles, byte servoCount) {
    byte buf[12];
    int idx = 0;
    buf[idx++] = stepper1State;
    buf[idx++] = stepper2State;
    buf[idx++] = stepper1Speed & 0xFF;
    buf[idx++] = stepper1Speed >> 8;
    buf[idx++] = stepper2Speed & 0xFF;
    buf[idx++] = stepper2Speed >> 8;
    for (byte i = 0; i < servoCount && i < 6; i++) {
        buf[idx++] = servoAngles[i];
    }
    _sendFrameInternal(BM53_CMD_MOTOR_STATUS, buf, idx);
}

void BM53_Protocol::sendPressureDataBatch(byte sensorCount, const byte* sensorIds,
                                          const float* currentValues, const float* maxValues,
                                          const float* thresholds, const byte* alertFlags) {
    const int recordLength = 14;
    const int MAX_PRESSURE_BUF = 1 + 8 * recordLength;
    byte buf[MAX_PRESSURE_BUF];
    uint16_t dataLen = 1 + sensorCount * recordLength;

    buf[0] = sensorCount;

    for (byte i = 0; i < sensorCount; i++) {
        int offset = 1 + i * recordLength;
        buf[offset] = sensorIds[i];
        memcpy(buf + offset + 1, &currentValues[i], 4);
        memcpy(buf + offset + 5, &maxValues[i], 4);
        memcpy(buf + offset + 9, &thresholds[i], 4);
        buf[offset + 13] = alertFlags[i];
    }

    _sendFrameInternal(BM53_CMD_PRESSURE, buf, dataLen);
}

// ============================================================
// CRC16计算
// ============================================================
uint16_t BM53_Protocol::crc16(byte* data, int len) {
    uint16_t crc = 0xFFFF;
    for (int i = 0; i < len; i++) {
        crc ^= data[i];
        for (int j = 0; j < 8; j++) {
            if (crc & 1) {
                crc = (crc >> 1) ^ 0xA001;
            } else {
                crc >>= 1;
            }
        }
    }
    return crc;
}

// ============================================================
// 辅助函数
// ============================================================
const char* BM53_Protocol::_getCmdName(byte cmd) {
    switch(cmd) {
        case BM53_CMD_HEARTBEAT:     return "心跳";
        case BM53_CMD_HANDSHAKE:     return "握手";
        case BM53_CMD_DEV_INFO:      return "设备信息";
        case BM53_CMD_START_THERAPY: return "开始治疗";
        case BM53_CMD_STOP_THERAPY:  return "停止治疗";
        case BM53_CMD_EME_STOP:      return "紧急停止";
        case BM53_CMD_PAUSE_THERAPY:   return "暂停治疗";
        case BM53_CMD_RESUME_THERAPY:  return "继续治疗";
        case BM53_CMD_EXIT_EME_STOP:   return "退出急停";
        case BM53_CMD_SET_MODE:      return "设置模式";
        case BM53_CMD_SET_INTENSITY: return "设置强度";
        case BM53_CMD_SET_DURATION:  return "设置时长";
        case BM53_CMD_STEPPER_CTRL:  return "步进电机";
        case BM53_CMD_SERVO_CTRL:    return "舵机控制";
        case BM53_CMD_SERVO_HOME:    return "舵机归位";
        case BM53_CMD_MOTOR_HOME:      return "电机回零";
        case BM53_CMD_MOTOR_STEP:      return "电机步进";
        case BM53_CMD_MOTOR_POSITION:  return "读取位置";
        case BM53_CMD_SET_TIME:        return "设置时间";
        case BM53_CMD_PRESSURE:      return "压力数据";
        case BM53_CMD_EDEMA:         return "水肿数据";
        case BM53_CMD_MOTOR_STATUS:  return "电机状态";
        case BM53_CMD_PROGRESS:      return "治疗进度";
        case BM53_CMD_ACK:           return "ACK";
        case BM53_CMD_NAK:           return "NAK";
        default:                     return "未知";
    }
}

void BM53_Protocol::_printHex(byte b) {
    if (!_debugStream) return;
    if (b < 0x10) _debugStream->print('0');
    _debugStream->print(b, HEX);
}

void BM53_Protocol::_printHexArray(byte* data, int len, int maxPrint) {
    if (!_debugStream) return;
    int printLen = min(len, maxPrint);
    for (int i = 0; i < printLen; i++) {
        if (i > 0) _debugStream->print(' ');
        _printHex(data[i]);
    }
    if (len > maxPrint) {
        _debugStream->print(F(" ... (共"));
        _debugStream->print(len);
        _debugStream->print(F("字节)"));
    }
}

void BM53_Protocol::_printTimestamp() {
    if (!_debugStream) return;
    unsigned long t = millis();
    _debugStream->print('[');
    _debugStream->print(t / 1000);
    _debugStream->print('.');
    if (t % 1000 < 100) _debugStream->print('0');
    if (t % 1000 < 10) _debugStream->print('0');
    _debugStream->print(t % 1000);
    _debugStream->print(F("] "));
}