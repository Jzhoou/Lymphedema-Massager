/*
 * ================================================================
 * TimeStamp.cpp — 系统时间管理模块实现
 * ================================================================
 */

#include "TimeStamp.h"

// ============================================================
// 全局时间戳对象
// ============================================================
TimeStamp systemTime;

// ============================================================
// 构造函数
// ============================================================
TimeStamp::TimeStamp()
    : _baseTime(0)
    , _syncMillis(0)
    , _synced(false)
{
}

// ============================================================
// 初始化
// ============================================================
void TimeStamp::begin() {
    _baseTime = 0;
    _syncMillis = 0;
    _synced = false;
    Serial.println(F("[时间戳] 模块初始化完成，等待上位机同步..."));
}

// ============================================================
// 同步系统时间
// ============================================================
void TimeStamp::syncTime(unsigned long long unixTimeMs) {
    _baseTime = unixTimeMs;
    _syncMillis = millis();
    _synced = true;
    
    Serial.print(F("[时间戳] 时间同步成功: "));
    unsigned long seconds = (unsigned long)(unixTimeMs / 1000ULL);
    Serial.print(seconds);
    Serial.println(F(" (Unix秒)"));
}

// ============================================================
// 获取当前系统时间（毫秒）
// ============================================================
unsigned long long TimeStamp::getCurrentTimeMs() {
    if (!_synced) {
        return 0;
    }
    unsigned long elapsed = millis() - _syncMillis;
    return _baseTime + (unsigned long long)elapsed;
}

// ============================================================
// 获取时间戳数据总长度
// ============================================================
uint8_t TimeStamp::getTimestampLength() const {
    return _synced ? TIMESTAMP_LEN_ABS : TIMESTAMP_LEN_REL;
}

// ============================================================
// 获取时间戳写入缓冲区
// ============================================================
uint8_t TimeStamp::getTimestampBytes(byte* buffer) {
    if (_synced) {
        // 绝对时间：flag(1) + timestamp(8)
        buffer[0] = TIMESTAMP_FLAG_ABS;
        unsigned long long timestamp = getCurrentTimeMs();
        for (uint8_t i = 0; i < 8; i++) {
            buffer[1 + i] = (byte)((timestamp >> (i * 8)) & 0xFF);
        }
        return TIMESTAMP_LEN_ABS;
    } else {
        // 相对时间：flag(1) + timestamp(4)
        buffer[0] = TIMESTAMP_FLAG_REL;
        unsigned long timestamp = millis();
        for (uint8_t i = 0; i < 4; i++) {
            buffer[1 + i] = (byte)((timestamp >> (i * 8)) & 0xFF);
        }
        return TIMESTAMP_LEN_REL;
    }
}