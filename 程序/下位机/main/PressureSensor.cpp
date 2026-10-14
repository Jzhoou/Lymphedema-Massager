/*
 * ================================================================
 * PressureSensor.cpp — 压力传感器实现 (优化版)
 * ================================================================
 */

#include "PressureSensor.h"

// -------------------- 构造函数 --------------------
PressureSensor::PressureSensor()
    : _calCount(0), _baseline(BASELINE_AD) {
}

// -------------------- 初始化 --------------------
void PressureSensor::begin() {
    // 加载校准数据
    _calCount = min((uint8_t)CAL_POINTS, (uint8_t)MAX_CAL_POINTS);
    
    for (uint8_t i = 0; i < _calCount; i++) {
        _calAD[i] = CAL_AD[i];
        _calForce[i] = CAL_FORCE[i];
    }
    
    _baseline = BASELINE_AD;
    
    // 验证校准表
    if (!validateCalibration()) {
        // 校准表有问题，使用默认线性映射
        _calCount = 2;
        _calAD[0] = 0;
        _calAD[1] = 4095;
        _calForce[0] = 0;
        _calForce[1] = FORCE_MAX;
    }
}

// -------------------- AD值转压力值 --------------------
long PressureSensor::adToForce(uint16_t adValue) {
    // 底噪过滤
    if (adValue <= _baseline) {
        return 0;
    }
    
    // 数据不足
    if (_calCount < 2) {
        return 0;
    }
    
    // 低于最小校准点
    if (adValue <= _calAD[0]) {
        return 0;
    }
    
    // 超过最大校准点
    if (adValue >= _calAD[_calCount - 1]) {
        return _calForce[_calCount - 1];
    }
    
    // 分段线性插值
    for (uint8_t i = 0; i < _calCount - 1; i++) {
        if (adValue <= _calAD[i + 1]) {
            return interpolate(adValue, i);
        }
    }
    
    return 0;
}

// -------------------- 线性插值 --------------------
long PressureSensor::interpolate(uint16_t ad, uint8_t idx) {
    // 公式: F = F1 + (AD - AD1) * (F2 - F1) / (AD2 - AD1)
    
    long ad1 = _calAD[idx];
    long ad2 = _calAD[idx + 1];
    long f1 = _calForce[idx];
    long f2 = _calForce[idx + 1];
    
    // 防止除零
    if (ad2 == ad1) {
        return f1;
    }
    
    // 线性插值
    long force = f1 + ((long)(ad - ad1) * (f2 - f1)) / (ad2 - ad1);
    
    // 限制在合理范围
    if (force < 0) force = 0;
    if (force > FORCE_MAX) force = FORCE_MAX;
    
    return force;
}

// -------------------- 批量转换 --------------------
void PressureSensor::batchConvert(uint16_t *adArray, long *forceArray, uint8_t count) {
    for (uint8_t i = 0; i < count; i++) {
        forceArray[i] = adToForce(adArray[i]);
    }
}

// -------------------- 验证校准表 --------------------
bool PressureSensor::validateCalibration() {
    if (_calCount < 2) {
        return false;
    }
    
    // 检查AD值是否递增
    for (uint8_t i = 0; i < _calCount - 1; i++) {
        if (_calAD[i] >= _calAD[i + 1]) {
            return false;
        }
    }
    
    // 检查压力值是否递增
    for (uint8_t i = 0; i < _calCount - 1; i++) {
        if (_calForce[i] > _calForce[i + 1]) {
            return false;
        }
    }
    
    return true;
}

// -------------------- 获取校准信息 --------------------
void PressureSensor::getCalibrationInfo(char *buffer, size_t bufferSize) {
    snprintf(buffer, bufferSize, 
             "校准点数:%d, 基线:%d, 范围:%d-%dg",
             _calCount, _baseline, _calForce[0], _calForce[_calCount-1]);
}