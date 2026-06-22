#include "motor_control.h"

/************************************************
 * 全局指令数组
 ************************************************/
byte together_Motor[] = {0x00, 0xFF, 0x66, 0x6B};

/************************************************
 * ========== 到位检测 & 运动状态 ==========
 ************************************************/
static unsigned long _motorMoveStartTime = 0;
static bool _motorMoving = false;
static bool _motorReached[2] = {false, false};
static bool _motorReachedValid[2] = {false, false};

#define MOTOR_MOVE_BLIND_MS 200
#define MOTOR_STATUS_TIMEOUT_MS 300
#define MOTOR_STATUS_POLL_INTERVAL_MS 200

/************************************************
 * ========== 状态轮询状态机 ==========
 ************************************************/
enum PollState {
    POLL_IDLE,
    POLL_SENDING,
    POLL_WAITING
};

static PollState _pollState = POLL_IDLE;
static unsigned long _pollSendTime = 0;
static unsigned long _pollLastCycleTime = 0;

// 轮询接收缓冲（最大帧长8字节，足够状态帧4字节）
static byte _pollRxBuf[8];
static uint8_t _pollRxCount = 0;

/************************************************
 * ========== 位置缓存 ==========
 ************************************************/
static long _cachedPosition = -1;
static bool _cachedPositionValid = false;
static unsigned long _lastPositionReadTime = 0;
#define POSITION_CACHE_MS 300  // 300ms内重复调用返回缓存值

/************************************************
 * 内部函数声明
 ************************************************/
static void sendMotorCmd(byte ID, byte dir, byte v1, byte v2, byte a,
                         byte d1, byte d2, byte d3, byte d4, byte absolute);

/************************************************
 * ★ 清空串口接收缓冲区
 ************************************************/
static void clearRxBuf() {
    while (MOTOR_SERIAL.available()) {
        MOTOR_SERIAL.read();
    }
}

// ==================== 位置读取 ====================

/**
 * @brief  阻塞式读取电机当前位置
 */
static long motorReadPositionBlocking(byte motorId) {
    clearRxBuf();
    byte cmd[] = {motorId, 0x36, MOTOR_FIXED_CHECKSUM};
    MOTOR_SERIAL.write(cmd, sizeof(cmd));

    unsigned long t0 = millis();
    byte frame[8];
    uint8_t fi = 0;

    while (millis() - t0 < 500) {
        while (MOTOR_SERIAL.available()) {
            byte b = MOTOR_SERIAL.read();
            if (fi == 0) {
                if (b == motorId) frame[fi++] = b;
                continue;
            }
            if (fi == 1) {
                if (b == 0x36) {
                    frame[fi++] = b;
                } else {
                    fi = (b == motorId) ? 1 : 0;
                    if (fi == 1) frame[0] = b;
                }
                continue;
            }
            frame[fi++] = b;
            if (fi == 8) {
                fi = 0;
                if (frame[7] != MOTOR_FIXED_CHECKSUM) continue;
                byte sign = frame[2];
                long raw = ((long)frame[3] << 24) |
                           ((long)frame[4] << 16) |
                           ((long)frame[5] << 8) |
                           (long)frame[6];
                return (sign == 0x01) ? -raw : raw;
            }
        }
        delay(1);
    }
    return -1;
}

/**
 * @brief  获取电机位置（带缓存：短时间内返回缓存值）
 */
long motor_get_cached_position(byte motorId) {
    unsigned long now = millis();
    if (_cachedPositionValid && (now - _lastPositionReadTime) < POSITION_CACHE_MS) {
        return _cachedPosition;
    }
    
    long pos = motorReadPositionBlocking(0x04);
    if (pos >= 0) {
        _cachedPosition = pos;
        _cachedPositionValid = true;
        _lastPositionReadTime = now;
    }
    return pos;
}

/**
 * @brief  检查缓存位置是否有效
 */
bool motor_is_position_valid(byte motorId) {
    return _cachedPositionValid;
}

/**
 * @brief  批量读取所有电机位置
 */
uint8_t motor_read_all_positions(long* positions) {
    long pos = motor_get_cached_position(0x04);
    positions[0] = pos;
    positions[1] = pos;
    return (pos >= 0) ? 2 : 0;
}

// ==================== 状态轮询实现 ====================

/**
 * @brief  状态轮询状态机（在loop中调用）
 * @details 专用状态查询：只发 0x3A，只解析 4字节固定格式
 *          与位置查询完全隔离，不会互相干扰
 */
void motor_poll_positions() {
    switch (_pollState) {
        case POLL_IDLE:
            if (millis() - _pollLastCycleTime >= MOTOR_STATUS_POLL_INTERVAL_MS) {
                _pollState = POLL_SENDING;
            }
            break;

        case POLL_SENDING: {
            // ★ 发送前清空缓冲区，确保干净状态
            clearRxBuf();
            _pollRxCount = 0;
            
            // 发送状态查询命令：{0x04, 0x3A, 0x6B}
            byte cmd[] = {0x04, 0x3A, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(cmd, sizeof(cmd));
            
            _pollSendTime = millis();
            _pollState = POLL_WAITING;
            break;
        }

        case POLL_WAITING: {
            // ★ 超时检查
            if (millis() - _pollSendTime > MOTOR_STATUS_TIMEOUT_MS) {
                clearRxBuf();
                Serial.println(F("[轮询] 状态读取超时"));
                _pollLastCycleTime = millis();
                _pollState = POLL_IDLE;
                break;
            }
            
            // 读取可用字节到缓冲区
            while (MOTOR_SERIAL.available() && _pollRxCount < 8) {
                _pollRxBuf[_pollRxCount++] = MOTOR_SERIAL.read();
            }
            
            // ★ 尝试解析状态帧（至少4字节）
            if (_pollRxCount >= 4) {
                // 在缓冲区中扫描 {0x04, 0x3A, status, 0x6B}
                for (uint8_t i = 0; i <= _pollRxCount - 4; i++) {
                    if (_pollRxBuf[i]     == 0x04 &&
                        _pollRxBuf[i + 1] == 0x3A &&
                        _pollRxBuf[i + 3] == MOTOR_FIXED_CHECKSUM) {
                        
                        byte status = _pollRxBuf[i + 2];
                        _motorReached[1] = (status & 0x02) != 0;
                        _motorReachedValid[1] = true;
                        
                        // 日志：状态变化时打印
                        static byte lastStatus = 0xFF;
                        if (status != lastStatus) {
                            Serial.print(F("[状态] 电机0x04 原始状态=0x"));
                            if (status < 0x10) Serial.print('0');
                            Serial.print(status, HEX);
                            Serial.print(F(" 使能="));
                            Serial.print((status & 0x01) ? "是" : "否");
                            Serial.print(F(" 到位="));
                            Serial.print((status & 0x02) ? "是" : "否");
                            Serial.print(F(" 堵转="));
                            Serial.print((status & 0x04) ? "是" : "否");
                            Serial.print(F(" 堵转保护="));
                            Serial.println((status & 0x08) ? "是" : "否");
                            lastStatus = status;
                        }
                        
                        _pollLastCycleTime = millis();
                        _pollState = POLL_IDLE;
                        break;
                    }
                }
                
                if (_pollState == POLL_WAITING) {
                    // 没找到有效帧，清空缓冲区重新开始
                    _pollRxCount = 0;
                }
            }
            break;
        }
    }
}

// ==================== 到位检测接口 ====================

bool motor_both_reached() {
    if (_motorMoving && (millis() - _motorMoveStartTime) < MOTOR_MOVE_BLIND_MS) {
        return false;
    }
    if (!_motorReachedValid[1]) return false;
    return _motorReached[1];
}

bool motor_get_cached_reached(byte motorId) {
    if (_motorReachedValid[1]) return _motorReached[1];
    return false;
}

bool motor_is_reached_valid() {
    return _motorReachedValid[1];
}

unsigned long motor_get_move_start_time() {
    return _motorMoveStartTime;
}

void motor_clear_reached_flag() {
    _motorReached[0] = false;
    _motorReached[1] = false;
    _motorReachedValid[0] = true;
    _motorReachedValid[1] = true;
    Serial.println(F("[到位] 到位标志已清除（运动开始）"));
}

void motor_invalidate_reached() {
    _motorReachedValid[0] = false;
    _motorReachedValid[1] = false;
    _motorMoving = false;
    _pollState = POLL_IDLE;
    _pollLastCycleTime = millis();
    clearRxBuf();
    Serial.println(F("[到位] 到位状态标记为待验证"));
}

void motor_notify_move_started() {
    _motorMoveStartTime = millis();
    _motorMoving = true;
    _pollState = POLL_IDLE;
    _pollLastCycleTime = millis();
    clearRxBuf();
    Serial.println(F("[电机] 运动开始，清空缓冲并屏蔽到位检测200ms"));
}

// ==================== 基础运动控制 ====================

static void sendMotorCmd(byte ID, byte dir, byte v1, byte v2, byte a,
                         byte d1, byte d2, byte d3, byte d4, byte absolute)
{
    byte cmd[13] = {
        ID, 0xFD, dir, v1, v2, a,
        d1, d2, d3, d4, absolute,
        0x01, MOTOR_FIXED_CHECKSUM
    };
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    delay(50);
}

static void splitSpeed(word speed, byte *v1, byte *v2) {
    *v1 = highByte(speed);
    *v2 = lowByte(speed);
}

void motor_init(void) {
    MOTOR_SERIAL.begin(MOTOR_BAUD_RATE);
    delay(1000);
}

void To_arm(void) {
    sendMotorCmd(0x02, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x06, 0x50, 0x00, 0x01);
    sendMotorCmd(0x04, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x06, 0x50, 0x00, 0x01);
    MOTOR_SERIAL.write(together_Motor, sizeof(together_Motor));
}

void back(void) {
    sendMotorCmd(0x02, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x01);
    sendMotorCmd(0x04, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x01);
    MOTOR_SERIAL.write(together_Motor, sizeof(together_Motor));
}

void motor_stop_all(void) {
    byte stopCmdAll[] = {0x00, 0xFE, 0x98, 0x00, 0x6B};
    MOTOR_SERIAL.write(stopCmdAll, sizeof(stopCmdAll));
}

// ==================== 回零 ====================

void motor_homing(void) {
    Serial.begin(115200);
    Serial.println("\n========== 开始回零 ==========");

    pinMode(LIMIT_SWITCH_2_PIN, INPUT_PULLUP);
    pinMode(LIMIT_SWITCH_4_PIN, INPUT_PULLUP);
    delay(100);

    bool switch2_triggered = (digitalRead(LIMIT_SWITCH_2_PIN) == LOW);
    bool switch4_triggered = (digitalRead(LIMIT_SWITCH_4_PIN) == LOW);
    Serial.print("D4(2号电机限位)初始电平: "); Serial.println(switch2_triggered ? "LOW(已在零点)" : "HIGH(需要回零)");
    Serial.print("D5(4号电机限位)初始电平: "); Serial.println(switch4_triggered ? "LOW(已在零点)" : "HIGH(需要回零)");

    if (switch2_triggered) {
        Serial.println("2号电机: 已在零点，直接清零位置");
        byte cmd2_zero[] = {0x02, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
        MOTOR_SERIAL.write(cmd2_zero, sizeof(cmd2_zero));
        delay(50);
    }
    if (switch4_triggered) {
        Serial.println("4号电机: 已在零点，直接清零位置");
        byte cmd4_zero[] = {0x04, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
        MOTOR_SERIAL.write(cmd4_zero, sizeof(cmd4_zero));
        delay(50);
    }
    if (switch2_triggered && switch4_triggered) {
        Serial.println("========== 回零完成(全部已在零点) ==========\n");
        return;
    }

    word speed = HOMING_SPEED * 60;

    if (!switch2_triggered) {
        Serial.print("2号电机: 开始反转回零... ");
        byte v1, v2;
        splitSpeed(speed, &v1, &v2);
        byte cmd2_run[] = { 0x02, 0xF6, 0x00, v1, v2, 0x00, 0x00, MOTOR_FIXED_CHECKSUM };
        MOTOR_SERIAL.write(cmd2_run, sizeof(cmd2_run));
        delay(50);
        Serial.println("速度命令已发送");
    }
    if (!switch4_triggered) {
        Serial.print("4号电机: 开始反转回零... ");
        byte v1, v2;
        splitSpeed(speed, &v1, &v2);
        byte cmd4_run[] = { 0x04, 0xF6, 0x00, v1, v2, 0x00, 0x00, MOTOR_FIXED_CHECKSUM };
        MOTOR_SERIAL.write(cmd4_run, sizeof(cmd4_run));
        delay(50);
        Serial.println("速度命令已发送");
    }

    unsigned long startTime = millis();
    const unsigned long timeout = 30000;
    Serial.print("开始监测限位(超时时间: "); Serial.print(timeout); Serial.println("ms)...");

    while (true) {
        if (!switch2_triggered && digitalRead(LIMIT_SWITCH_2_PIN) == LOW) {
            Serial.println("2号电机: 限位触发，立即停止并清零位置");
            byte stop2[] = {0x02, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(stop2, sizeof(stop2));
            delay(10);
            switch2_triggered = true;
        }
        if (!switch4_triggered && digitalRead(LIMIT_SWITCH_4_PIN) == LOW) {
            Serial.println("4号电机: 限位触发，立即停止并清零位置");
            byte stop4[] = {0x04, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(stop4, sizeof(stop4));
            delay(10);
            switch4_triggered = true;
        }
        if (switch2_triggered && switch4_triggered) {
            byte cmd2_zero[] = {0x02, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(cmd2_zero, sizeof(cmd2_zero));
            delay(50);
            Serial.println("2号电机: 回零完成");
            byte cmd4_zero[] = {0x04, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(cmd4_zero, sizeof(cmd4_zero));
            delay(50);
            Serial.println("4号电机: 回零完成");
            Serial.println("========== 全部回零完成 ==========\n");
            break;
        }
        if (millis() - startTime > timeout) {
            Serial.println("!!! 回零超时 !!! 强制停止");
            if (!switch2_triggered) {
                byte stop2[] = {0x02, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
                MOTOR_SERIAL.write(stop2, sizeof(stop2));
                delay(50);
            }
            if (!switch4_triggered) {
                byte stop4[] = {0x04, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
                MOTOR_SERIAL.write(stop4, sizeof(stop4));
                delay(50);
            }
            break;
        }
        delay(1);
    }
}

// ==================== 步进控制 ====================

void motor_step(byte motorId, byte direction, unsigned long steps, word speed) {
    byte v1 = highByte(speed);
    byte v2 = lowByte(speed);
    byte cmd[13] = {
        motorId, 0xFD, direction, v1, v2, 0x8C,
        (byte)(steps >> 24), (byte)(steps >> 16),
        (byte)(steps >> 8), (byte)steps,
        0x01, 0x00, MOTOR_FIXED_CHECKSUM
    };
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    delay(10);
}

void motor_step_sync(byte direction, unsigned long steps, word speed) {
    byte v1 = highByte(speed);
    byte v2 = lowByte(speed);
    byte d1 = (byte)(steps >> 24);
    byte d2 = (byte)(steps >> 16);
    byte d3 = (byte)(steps >> 8);
    byte d4 = (byte)steps;

    byte cmd2[13] = { 0x02, 0xFD, direction, v1, v2, 0x8C,
                      d1, d2, d3, d4, 0x01, 0x01, MOTOR_FIXED_CHECKSUM };
    MOTOR_SERIAL.write(cmd2, sizeof(cmd2));
    delay(50);

    byte cmd4[13] = { 0x04, 0xFD, direction, v1, v2, 0x8C,
                      d1, d2, d3, d4, 0x01, 0x01, MOTOR_FIXED_CHECKSUM };
    MOTOR_SERIAL.write(cmd4, sizeof(cmd4));
    delay(50);

    MOTOR_SERIAL.write(together_Motor, sizeof(together_Motor));

    Serial.print(F("[电机] 双电机同步步进: 方向="));
    Serial.print(direction == 0x00 ? F("伸出") : F("收回"));
    Serial.print(F(" 步数="));
    Serial.print(steps);
    Serial.print(F(" 速度="));
    Serial.print(speed);
    Serial.println(F("RPM"));
}

void motor_step_async(byte direction, unsigned long steps, word speed) {
    byte v1 = highByte(speed);
    byte v2 = lowByte(speed);
    byte d1 = (byte)(steps >> 24);
    byte d2 = (byte)(steps >> 16);
    byte d3 = (byte)(steps >> 8);
    byte d4 = (byte)steps;

    byte cmd2[13] = { 0x02, 0xFD, direction, v1, v2, 0x8C,
                      d1, d2, d3, d4, 0x01, 0x00, MOTOR_FIXED_CHECKSUM };
    MOTOR_SERIAL.write(cmd2, sizeof(cmd2));
    delay(10);

    byte cmd4[13] = { 0x04, 0xFD, direction, v1, v2, 0x8C,
                      d1, d2, d3, d4, 0x01, 0x00, MOTOR_FIXED_CHECKSUM };
    MOTOR_SERIAL.write(cmd4, sizeof(cmd4));
    delay(10);

    Serial.print(F("[电机] 双电机异步步进: 方向="));
    Serial.print(direction == 0x00 ? F("伸出") : F("收回"));
    Serial.print(F(" 步数="));
    Serial.print(steps);
    Serial.print(F(" 速度="));
    Serial.print(speed);
    Serial.println(F("RPM"));
}

// ==================== 兼容接口 ====================

long motor_read_position(byte motorId) {
    return motorReadPositionBlocking(motorId);
}

long get_last_motor_position() {
    return _cachedPosition;
}

bool has_new_position_data() {
    return _cachedPositionValid;
}