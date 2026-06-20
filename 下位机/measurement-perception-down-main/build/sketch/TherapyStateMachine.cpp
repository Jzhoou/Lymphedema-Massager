#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\TherapyStateMachine.cpp"
/*
 * ================================================================
 * TherapyStateMachine.cpp — 治疗状态机实现
 * ================================================================
 */

#include "TherapyStateMachine.h"

// ============================================================
// 构造函数
// ============================================================
TherapyStateMachine::TherapyStateMachine()
    : _currentState(STATE_IDLE)
    , _previousState(STATE_IDLE)
    , _therapyStartTime(0)
    , _therapyEndTime(0)
    , _pauseStartTime(0)
    , _totalPauseTime(0)
    , _therapyDuration(30)
    , _wasConnected(true)
    , _disconnectTime(0)
    , _disconnectTimeout(10000)
    , _autoStopDueToDisconnect(false)
    , _stateChangeCallback(nullptr)
{
}

// ============================================================
// 初始化
// ============================================================
void TherapyStateMachine::begin() {
    _currentState = STATE_IDLE;
    _previousState = STATE_IDLE;
    _autoStopDueToDisconnect = false;
    _totalPauseTime = 0;
}

// ============================================================
// 主循环更新
// ============================================================
TherapyState TherapyStateMachine::update() {
    unsigned long currentTime = millis();

    // 检查治疗是否超时（仅在运行状态）
    if (_currentState == STATE_RUNNING) {
        // 考虑暂停时间，调整结束时间判断
        unsigned long adjustedTime = currentTime - _totalPauseTime;
        if (_therapyEndTime > 0 && adjustedTime >= _therapyEndTime) {
            _transitionTo(STATE_COMPLETED);
        }
    }

    return _currentState;
}

// ============================================================
// 开始治疗
// ============================================================
bool TherapyStateMachine::start(TherapyConfig& config) {
    if (_currentState == STATE_EMERGENCY_STOP) {
        return false;  // 急停状态下不允许开始
    }

    // 保存配置
    _config = config;
    _therapyDuration = config.duration;
    if (_therapyDuration == 0) _therapyDuration = 30;

    // 记录开始时间
    _therapyStartTime = millis();
    _therapyEndTime = _therapyStartTime + (unsigned long)_therapyDuration * 60000UL;
    _totalPauseTime = 0;
    _autoStopDueToDisconnect = false;
    
    // ★ 重置断连追踪，防止之前的断连状态误触发自动停止
    _wasConnected = true;
    _disconnectTime = millis();

    _transitionTo(STATE_RUNNING);
    return true;
}

// ============================================================
// 暂停治疗
// ============================================================
bool TherapyStateMachine::pause() {
    if (_currentState != STATE_RUNNING) {
        return false;
    }

    _pauseStartTime = millis();
    _transitionTo(STATE_PAUSED);
    return true;
}

// ============================================================
// 继续治疗
// ============================================================
bool TherapyStateMachine::resume() {
    if (_currentState != STATE_PAUSED) {
        return false;
    }

    // 计算暂停时长
    if (_pauseStartTime > 0) {
        _totalPauseTime += (millis() - _pauseStartTime);
        _pauseStartTime = 0;
    }

    // 重新计算结束时间
    _recalculateEndTime();

    _transitionTo(STATE_RUNNING);
    return true;
}

// ============================================================
// 停止治疗
// ============================================================
bool TherapyStateMachine::stop() {
    if (_currentState != STATE_RUNNING && _currentState != STATE_PAUSED) {
        return false;
    }

    _transitionTo(STATE_IDLE);
    return true;
}

// ============================================================
// 紧急停止
// ============================================================
bool TherapyStateMachine::emergencyStop() {
    if (_currentState == STATE_EMERGENCY_STOP) {
        return false;
    }

    _transitionTo(STATE_EMERGENCY_STOP);
    return true;
}

// ============================================================
// 退出急停状态
// ============================================================
bool TherapyStateMachine::exitEmergencyStop() {
    if (_currentState != STATE_EMERGENCY_STOP) {
        return false;
    }

    _transitionTo(STATE_IDLE);
    return true;
}

// ============================================================
// 更新连接状态
// ============================================================
void TherapyStateMachine::updateConnectionStatus(bool connected, unsigned long disconnectDuration) {
    // 检测连接状态变化
    if (_wasConnected && !connected) {
        // 从连接变为断开
        _disconnectTime = millis();
    } else if (!_wasConnected && connected) {
        // 从断开变为连接
        _autoStopDueToDisconnect = false;
    }

    // 如果治疗进行中且断开连接超过超时时间
    if (isTherapyActive() && !connected) {
        if (disconnectDuration >= _disconnectTimeout) {
            _autoStopDueToDisconnect = true;
            _transitionTo(STATE_IDLE);  // 自动停止
        }
    }

    _wasConnected = connected;
}

// ============================================================
// 获取已用时间（秒）
// ============================================================
unsigned long TherapyStateMachine::getElapsedSeconds() const {
    if (_therapyStartTime == 0) return 0;

    unsigned long elapsed = (millis() - _therapyStartTime - _totalPauseTime) / 1000;

    // 如果在暂停中，不计算暂停期间的时间
    if (_currentState == STATE_PAUSED && _pauseStartTime > 0) {
        elapsed -= (millis() - _pauseStartTime) / 1000;
    }

    return elapsed;
}

// ============================================================
// 获取剩余时间（秒）
// ============================================================
unsigned long TherapyStateMachine::getRemainingSeconds() const {
    unsigned long total = getTotalSeconds();
    unsigned long elapsed = getElapsedSeconds();

    return (total > elapsed) ? (total - elapsed) : 0;
}

// ============================================================
// 获取进度百分比
// ============================================================
uint8_t TherapyStateMachine::getProgressPercent() const {
    unsigned long total = getTotalSeconds();
    if (total == 0) return 0;

    unsigned long elapsed = getElapsedSeconds();
    uint8_t percent = (uint8_t)((elapsed * 100) / total);
    
    return (percent > 100) ? 100 : percent;
}

// ============================================================
// 治疗是否已超时
// ============================================================
bool TherapyStateMachine::isTimeUp() const {
    if (_currentState != STATE_RUNNING) return false;
    if (_therapyEndTime == 0) return false;

    return (millis() - _totalPauseTime) >= _therapyEndTime;
}

// ============================================================
// 设置治疗模式
// ============================================================
void TherapyStateMachine::setMode(uint8_t mode) {
    _config.mode = mode;
    _config.configChanged = true;
}

// ============================================================
// 设置治疗强度
// ============================================================
void TherapyStateMachine::setIntensity(uint8_t intensity) {
    _config.intensity = intensity;
    _config.configChanged = true;
}

// ============================================================
// 设置治疗时长
// ============================================================
void TherapyStateMachine::setDuration(uint16_t duration) {
    _config.duration = (duration == 0) ? 30 : duration;
    _therapyDuration = _config.duration;
    _config.configChanged = true;

    // 如果治疗正在进行，重新计算结束时间
    if (_currentState == STATE_RUNNING || _currentState == STATE_PAUSED) {
        _recalculateEndTime();
    }
}

// ============================================================
// 获取状态名称字符串
// ============================================================
const char* TherapyStateMachine::getStateName() const {
    return _stateToString(_currentState);
}

// ============================================================
// 内部：状态转换
// ============================================================
void TherapyStateMachine::_transitionTo(TherapyState newState) {
    if (_currentState == newState) return;

    TherapyState oldState = _currentState;
    _currentState = newState;

    // 调用回调
    if (_stateChangeCallback) {
        _stateChangeCallback(oldState, newState);
    }
}

// ============================================================
// 内部：重新计算结束时间
// ============================================================
void TherapyStateMachine::_recalculateEndTime() {
    _therapyEndTime = _therapyStartTime + (unsigned long)_therapyDuration * 60000UL + _totalPauseTime;
}

// ============================================================
// 内部：状态转字符串
// ============================================================
const char* TherapyStateMachine::_stateToString(TherapyState state) const {
    switch (state) {
        case STATE_IDLE:            return "IDLE";
        case STATE_RUNNING:         return "RUNNING";
        case STATE_PAUSED:          return "PAUSED";
        case STATE_EMERGENCY_STOP:  return "EMERGENCY_STOP";
        case STATE_COMPLETED:       return "COMPLETED";
        default:                    return "UNKNOWN";
    }
}