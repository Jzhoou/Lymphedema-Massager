/*
 * ================================================================
 * TimeStamp.h — 系统时间管理模块
 * ================================================================
 * 接收上位机发来的NTP时间，维持内部时钟
 * 为所有上报数据帧添加时间戳
 * 
 * 时间戳格式（与原WiFi中转模块兼容）：
 *   [flag(1)] [timestamp(8或4)]
 *   flag=0x01: 绝对时间（Unix毫秒，8字节）
 *   flag=0x00: 相对时间（millis，4字节）
 */

#ifndef TIMESTAMP_H
#define TIMESTAMP_H

#include <Arduino.h>

// ============================================================
// 时间戳标志位
// ============================================================
#define TIMESTAMP_FLAG_ABS  0x01   // 绝对时间（Unix毫秒）
#define TIMESTAMP_FLAG_REL  0x00   // 相对时间（millis）

// ============================================================
// 时间戳数据长度
// ============================================================
#define TIMESTAMP_LEN_ABS   9      // flag(1) + timestamp(8)
#define TIMESTAMP_LEN_REL   5      // flag(1) + timestamp(4)

// ============================================================
// TimeStamp 类
// ============================================================
class TimeStamp {
public:
    /**
     * @brief 构造函数
     */
    TimeStamp();

    /**
     * @brief 初始化时间模块
     */
    void begin();

    /**
     * @brief 同步系统时间（接收上位机NTP时间）
     * @param unixTimeMs Unix毫秒时间戳（8字节）
     */
    void syncTime(unsigned long long unixTimeMs);

    /**
     * @brief 获取当前系统时间（毫秒）
     * @return Unix毫秒时间戳，未同步时返回0
     */
    unsigned long long getCurrentTimeMs();

    /**
     * @brief 时间是否已同步
     */
    bool isSynchronized() const { return _synced; }

    /**
     * @brief 获取时间戳写入缓冲区
     * @param buffer 输出缓冲区（至少9字节）
     * @return 写入的字节数（已同步9，未同步5）
     */
    uint8_t getTimestampBytes(byte* buffer);

    /**
     * @brief 获取时间戳数据总长度（flag + timestamp）
     * @return 字节数（9或5）
     */
    uint8_t getTimestampLength() const;

    /**
     * @brief 获取上次同步时间（millis()值）
     */
    unsigned long getLastSyncMillis() const { return _syncMillis; }

private:
    unsigned long long _baseTime;    // NTP同步时的基准时间（Unix毫秒）
    unsigned long _syncMillis;       // NTP同步时的millis()值
    bool _synced;                    // 是否已同步
};

// 全局时间戳对象声明
extern TimeStamp systemTime;

#endif // TIMESTAMP_H