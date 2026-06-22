/*
 * ================================================================
 * DataReporter.cpp — 数据上报模块实现
 * ================================================================
 */

#include "DataReporter.h"

// ============================================================
// 构造函数
// ============================================================
DataReporter::DataReporter(BM53_Protocol* protocol, ServoController* servo)
    : _protocol(protocol)
    , _servo(servo)
    , _lastPressureReport(0)
    , _lastEdemaReport(0)
    , _lastProgressReport(0)
    , _lastMotorReport(0)
    , _lastMotorPosReport(0)
    , _pressureDataChanged(false)
    , _motorStatusChanged(false)
    , _totalReports(0)
{
    // 初始化上次上报值数组
    for (int i = 0; i < USE_CHANNELS; i++) {
        _lastReportedForce[i] = 0;
        _forceWasNonZero[i] = false;
    }
}

// ============================================================
// 初始化
// ============================================================
void DataReporter::begin() {
    unsigned long now = millis();
    _lastPressureReport = now;
    _lastEdemaReport = now;
    _lastProgressReport = now;
    _lastMotorReport = now;
    _lastMotorPosReport = now;
    _pressureDataChanged = true;
    _motorStatusChanged = true;
}

// ============================================================
// 主更新函数
// ============================================================
void DataReporter::update(long* forceValues,
                          bool isTherapyActive,
                          bool motorState,
                          unsigned long therapyStartTime,
                          unsigned long therapyEndTime,
                          uint16_t therapyDuration,
                          bool bioCalSuccess,
                          double z20k,
                          double z50k)
{
    // 检查连接状态
    if (!_protocol->isConnected()) {
        return;
    }

    unsigned long currentTime = millis();

    // ========== 压力数据上报 ==========
    if (currentTime - _lastPressureReport >= REPORT_PRESSURE_INTERVAL || _pressureDataChanged) {
        _reportAllPressureData(forceValues);
        _lastPressureReport = currentTime;
        _pressureDataChanged = false;
        _totalReports++;
    }

    // ========== 水肿数据上报（仅在Back阶段，即 !motorState 时上报） ==========
    // To_arm阶段（motorState==true）未启动阻抗检测，Z20k/Z50k为旧数据，不应上报
    if (!motorState && currentTime - _lastEdemaReport >= REPORT_EDEMA_INTERVAL) {
        _reportEdemaData(bioCalSuccess, z20k, z50k);
        _lastEdemaReport = currentTime;
        _totalReports++;
    }

    // ========== 电机状态上报 ==========
    if (_motorStatusChanged) {
        _reportMotorStatus(motorState);
        _motorStatusChanged = false;
        _totalReports++;
    }

    // ========== 电机位置主动上报 ==========
    if (currentTime - _lastMotorPosReport >= REPORT_MOTOR_POS_INTERVAL) {
        _reportMotorPosition();
        _lastMotorPosReport = currentTime;
        _totalReports++;
    }

    // ========== 治疗进度上报 ==========
    if (currentTime - _lastProgressReport >= REPORT_PROGRESS_INTERVAL) {
        _reportProgress(isTherapyActive, therapyStartTime, therapyEndTime, therapyDuration);
        _lastProgressReport = currentTime;
        _totalReports++;
    }
}

// ============================================================
// 强制上报所有数据
// ============================================================
void DataReporter::forceReportAll() {
    _pressureDataChanged = true;
    _motorStatusChanged = true;
    
    // 重置上报计时器
    unsigned long now = millis();
    _lastPressureReport = now;
    _lastEdemaReport = now;
    _lastProgressReport = now;
    _lastMotorReport = now;
    _lastMotorPosReport = now;
    
    // 重置变化检测
    for (int i = 0; i < USE_CHANNELS; i++) {
        _lastReportedForce[i] = 0;
    }
}

// ============================================================
// 上报所有通道压力数据（批量发送，减少队列占用）
// ============================================================
void DataReporter::_reportAllPressureData(long* forceValues) {
    bool anyChanged = false;
    bool anyForceToZero = false;

    for (byte i = 0; i < USE_CHANNELS; i++) {
        long currentForce = forceValues[i];
        long lastForce = _lastReportedForce[i];

        // 检测数值变化（超过阈值）
        if (abs(currentForce - lastForce) >= FORCE_CHANGE_THRESHOLD) {
            anyChanged = true;
        }

        // 检测归零情况：上次非零，本次接近零
        if (lastForce > FORCE_ZERO_THRESHOLD && currentForce <= FORCE_ZERO_THRESHOLD) {
            anyForceToZero = true;
            _forceWasNonZero[i] = false;
        }

        // 记录是否曾为非零值
        if (currentForce > FORCE_ZERO_THRESHOLD) {
            _forceWasNonZero[i] = true;
        }
    }

    // 如果没有变化、没有归零、且不是强制上报，跳过
    if (!anyChanged && !anyForceToZero && !_pressureDataChanged) {
        return;
    }

    // 批量上报：将所有通道数据打包为一帧，大幅减少队列占用
    float maxValue = 100.0f;
    float threshold = 50.0f;
    
    byte sensorIds[USE_CHANNELS];
    float currentValues[USE_CHANNELS];
    float maxValues[USE_CHANNELS];
    float thresholds[USE_CHANNELS];
    byte alertFlags[USE_CHANNELS];
    
    for (byte i = 0; i < USE_CHANNELS; i++) {
        sensorIds[i] = i + 1;
        currentValues[i] = (float)forceValues[i];
        maxValues[i] = maxValue;
        thresholds[i] = threshold;
        alertFlags[i] = (currentValues[i] >= threshold) ? 2 : 0;
        _lastReportedForce[i] = forceValues[i];
    }
    
    _protocol->sendPressureDataBatch(USE_CHANNELS, sensorIds, currentValues, 
                                     maxValues, thresholds, alertFlags);
}

// ============================================================
// 上报水肿数据
// ============================================================
void DataReporter::_reportEdemaData(bool bioCalSuccess, double z20k, double z50k) {
    if (!bioCalSuccess) return;

    float edemaPercent = 0.0f;
    float impedance = 0.0f;
    byte alert = 0;

    if (z20k > 0 && z50k > 0) {
        edemaPercent = (z20k / z50k) * 100.0f;
    }
    impedance = (float)z50k;

    // 根据水肿百分比设置告警
    if (edemaPercent > 150.0f) {
        alert = 2;  // 高度水肿
    } else if (edemaPercent > 120.0f) {
        alert = 1;  // 轻度水肿
    }

    _protocol->sendEdemaData(1, impedance, edemaPercent, alert);
}

// ============================================================
// 上报治疗进度
// ============================================================
void DataReporter::_reportProgress(bool isTherapyActive,
                                   unsigned long therapyStartTime,
                                   unsigned long therapyEndTime,
                                   uint16_t therapyDuration)
{
    if (!isTherapyActive) return;

    unsigned long currentTime = millis();
    unsigned long elapsed = (currentTime - therapyStartTime) / 1000;
    unsigned long total = (unsigned long)therapyDuration * 60UL;
    unsigned long remaining = (total > elapsed) ? (total - elapsed) : 0;

    // 计算进度百分比
    byte percent = (total > 0) ? (byte)((elapsed * 100) / total) : 0;
    if (percent > 100) percent = 100;

    _protocol->sendProgressData(percent, (uint16_t)elapsed, (uint16_t)remaining);
}

// ============================================================
// 上报电机状态
// ============================================================
void DataReporter::_reportMotorStatus(bool motorState) {
    byte stepper1State = motorState ? 1 : 0;
    byte stepper2State = motorState ? 1 : 0;
    uint16_t speed = motorState ? 100 : 0;

    // 获取舵机真实角度
    float angles[6];
    _servo->getCurrentAngles(angles);

    // 转换为字节（0-255 对应 0-270°）
    byte servoAngles[6];
    for (int i = 0; i < 6; i++) {
        servoAngles[i] = (byte)(angles[i] * 255.0f / 270.0f);
    }

    _protocol->sendMotorStatus(stepper1State, stepper2State, speed, speed, servoAngles, 6);
}

// ============================================================
// 上报电机位置数据
// ============================================================
void DataReporter::_reportMotorPosition() {
    // 读取两个电机的缓存位置（非阻塞）
    // ★ 2号电机数据暂不可用，使用4号电机代替（两电机物理同步）
    long pos2 = motor_get_cached_position(0x04);  // 用4号代替2号
    long pos4 = motor_get_cached_position(0x04);

    // 组装数据：4字节(2号电机位置) + 4字节(4号电机位置)
    byte posData[8];
    posData[0] = (byte)(pos2 >> 24);
    posData[1] = (byte)(pos2 >> 16);
    posData[2] = (byte)(pos2 >> 8);
    posData[3] = (byte)(pos2);
    posData[4] = (byte)(pos4 >> 24);
    posData[5] = (byte)(pos4 >> 16);
    posData[6] = (byte)(pos4 >> 8);
    posData[7] = (byte)(pos4);
    
    _protocol->sendFrame(BM53_CMD_MOTOR_POSITION, posData, 8);
}
