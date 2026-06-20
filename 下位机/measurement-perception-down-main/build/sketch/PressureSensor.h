#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\PressureSensor.h"
/*
 * ================================================================
 * PressureSensor.h — 压力传感器转换模块 (优化版)
 * ================================================================
 */

#ifndef PRESSURE_SENSOR_H
#define PRESSURE_SENSOR_H

#include <Arduino.h>
#include "config.h"

#define MAX_CAL_POINTS 16

class PressureSensor {
public:
    PressureSensor();
    
    // 初始化
    void begin();
    
    // 单值转换
    long adToForce(uint16_t adValue);
    
    // 批量转换
    void batchConvert(uint16_t *adArray, long *forceArray, uint8_t count);
    
    // 获取校准信息
    void getCalibrationInfo(char *buffer, size_t bufferSize);
    
    // 验证校准表
    bool validateCalibration();

private:
    uint16_t _calAD[MAX_CAL_POINTS];
    uint16_t _calForce[MAX_CAL_POINTS];
    uint8_t _calCount;
    uint16_t _baseline;
    
    // 线性插值计算
    long interpolate(uint16_t ad, uint8_t segmentIndex);
};

#endif // PRESSURE_SENSOR_H