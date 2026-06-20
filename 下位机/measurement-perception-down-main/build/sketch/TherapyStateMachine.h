#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\TherapyStateMachine.h"
/*
 * ================================================================
 * TherapyStateMachine.h — 治疗状态机模块
 * ================================================================
 * 管理治疗全生命周期状态：
 *   IDLE → RUNNING → PAUSED → RUNNING → COMPLETED
 *                   → STOPPED
 *   IDLE → EMERGENCY_STOP → IDLE (退出急停)
 * 
 * 同时管理连接超时自动停止、治疗时间计算等功能
 */

#ifndef THERAPY_STATE_MACHINE_H
#define THERAPY_STATE_MACHINE_H

#include <Arduino.h>

// ============================================================
// 治疗状态枚举
// ============================================================
enum TherapyState {
    STATE_IDLE = 0,             // 空闲（未开始治疗）
    STATE_RUNNING,              // 治疗运行中
    STATE_PAUSED,               // 治疗暂停
    STATE_EMERGENCY_STOP,       // 紧急停止
    STATE_COMPLETED             // 治疗完成
};

// ============================================================
// 状态变化回调函数类型
// ============================================================
typedef void (*StateChangeCallback)(TherapyState oldState, TherapyState newState);

// ============================================================
// 治疗配置结构体
// ============================================================
struct TherapyConfig {
    uint8_t mode = 0;           // 治疗模式 (0=默认, 1=循环, 2=持续...)
    uint8_t intensity = 50;     // 治疗强度 (0-100%)
    uint16_t duration = 30;     // 治疗时长 (分钟)
    bool configChanged = false; // 配置是否变更
};

// ============================================================
// 治疗状态机类
// ============================================================
class TherapyStateMachine {
public:
    /**
     * @brief 构造函数
     */
    TherapyStateMachine();

    /**
     * @brief 初始化状态机
     */
    void begin();

    /**
     * @brief 主循环更新（需要在 loop 中调用）
     * @details 处理超时检测、自动完成等逻辑
     * @return 当前状态
     */
    TherapyState update();

    // ========== 状态转换命令 ==========

    /**
     * @brief 开始治疗
     * @param config 治疗配置参数
     * @return true:成功 false:失败（如急停状态下不能开始）
     */
    bool start(TherapyConfig& config);

    /**
     * @brief 暂停治疗
     * @return true:成功 false:失败（非运行状态不能暂停）
     */
    bool pause();

    /**
     * @brief 继续治疗（从暂停恢复）
     * @return true:成功 false:失败（非暂停状态不能继续）
     */
    bool resume();

    /**
     * @brief 停止治疗（正常停止）
     * @return true:成功 false:失败
     */
    bool stop();

    /**
     * @brief 紧急停止
     * @return true:成功 false:已处于急停状态
     */
    bool emergencyStop();

    /**
     * @brief 退出急停状态
     * @return true:成功 false:非急停状态
     */
    bool exitEmergencyStop();

    // ========== 连接超时管理 ==========

    /**
     * @brief 通知连接状态变化
     * @param connected 当前是否连接
     * @param disconnectDuration 断开持续时长（毫秒）
     */
    void updateConnectionStatus(bool connected, unsigned long disconnectDuration);

    /**
     * @brief 获取是否因断连而自动停止
     */
    bool isAutoStopDueToDisconnect() const { return _autoStopDueToDisconnect; }

    // ========== 状态查询 ==========

    /**
     * @brief 获取当前状态
     */
    TherapyState getState() const { return _currentState; }

    /**
     * @brief 获取状态名称字符串
     */
    const char* getStateName() const;

    /**
     * @brief 是否正在治疗（RUNNING 或 PAUSED）
     */
    bool isTherapyActive() const {
        return _currentState == STATE_RUNNING || _currentState == STATE_PAUSED;
    }

    /**
     * @brief 是否处于急停状态
     */
    bool isEmergencyStopped() const {
        return _currentState == STATE_EMERGENCY_STOP;
    }

    /**
     * @brief 是否处于暂停状态
     */
    bool isPaused() const {
        return _currentState == STATE_PAUSED;
    }

    // ========== 时间相关 ==========

    /**
     * @brief 获取治疗开始时间
     */
    unsigned long getStartTime() const { return _therapyStartTime; }

    /**
     * @brief 获取治疗结束时间
     */
    unsigned long getEndTime() const { return _therapyEndTime; }

    /**
     * @brief 获取已用时间（秒）
     */
    unsigned long getElapsedSeconds() const;

    /**
     * @brief 获取剩余时间（秒）
     */
    unsigned long getRemainingSeconds() const;

    /**
     * @brief 获取总时长（秒）
     */
    unsigned long getTotalSeconds() const { return (unsigned long)_therapyDuration * 60UL; }

    /**
     * @brief 获取进度百分比 (0-100)
     */
    uint8_t getProgressPercent() const;

    /**
     * @brief 治疗是否已超时
     */
    bool isTimeUp() const;

    // ========== 配置管理 ==========

    /**
     * @brief 获取当前治疗配置
     */
    const TherapyConfig& getConfig() const { return _config; }

    /**
     * @brief 设置治疗模式
     */
    void setMode(uint8_t mode);

    /**
     * @brief 设置治疗强度
     */
    void setIntensity(uint8_t intensity);

    /**
     * @brief 设置治疗时长（分钟）
     * @param duration 时长（0=使用默认30分钟）
     */
    void setDuration(uint16_t duration);

    // ========== 回调注册 ==========

    /**
     * @brief 注册状态变化回调
     * @param callback 回调函数指针
     */
    void onStateChange(StateChangeCallback callback) {
        _stateChangeCallback = callback;
    }

    // ========== 连接超时配置 ==========

    /**
     * @brief 设置断开连接超时时间
     * @param timeoutMs 超时毫秒数（默认10000）
     */
    void setDisconnectTimeout(unsigned long timeoutMs) {
        _disconnectTimeout = timeoutMs;
    }

private:
    TherapyState _currentState;          // 当前状态
    TherapyState _previousState;         // 上一个状态（用于暂停恢复）
    TherapyConfig _config;              // 治疗配置

    unsigned long _therapyStartTime;     // 治疗开始时间
    unsigned long _therapyEndTime;       // 治疗结束时间
    unsigned long _pauseStartTime;       // 暂停开始时间
    unsigned long _totalPauseTime;       // 累计暂停时间
    uint16_t _therapyDuration;           // 治疗时长（分钟）

    // 连接状态管理
    bool _wasConnected;                  // 上一轮连接状态
    unsigned long _disconnectTime;       // 断开连接的时间
    unsigned long _disconnectTimeout;    // 断开超时时间（毫秒）
    bool _autoStopDueToDisconnect;       // 是否因断连而自动停止

    // 回调
    StateChangeCallback _stateChangeCallback;

    // 内部方法
    void _transitionTo(TherapyState newState);
    void _recalculateEndTime();
    const char* _stateToString(TherapyState state) const;
};

#endif // THERAPY_STATE_MACHINE_H