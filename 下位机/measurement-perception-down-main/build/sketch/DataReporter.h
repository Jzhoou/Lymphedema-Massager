#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\DataReporter.h"
/*
 * ================================================================
 * DataReporter.h — 数据上报模块
 * ================================================================
 * 负责将所有传感器数据通过 BM53 协议上报到上位机
 * 包括：压力数据、水肿数据、电机状态、治疗进度
 */

#ifndef DATA_REPORTER_H
#define DATA_REPORTER_H

#include <Arduino.h>
#include "BM53_Protocol.h"
#include "ServoController.h"
#include "config.h"

// ============================================================
// 上报间隔配置（毫秒）
// ============================================================
#define REPORT_PRESSURE_INTERVAL  200     // 压力数据上报间隔 (5Hz)
#define REPORT_EDEMA_INTERVAL     1000    // 水肿数据上报间隔 (1Hz)
#define REPORT_PROGRESS_INTERVAL  1000    // 治疗进度上报间隔 (1Hz)
#define REPORT_MOTOR_INTERVAL     100     // 电机状态上报间隔 (10Hz)

// ============================================================
// 压力变化检测阈值
// ============================================================
#define FORCE_CHANGE_THRESHOLD    2       // 压力变化阈值 (g)
#define FORCE_ZERO_THRESHOLD      2       // 归零检测阈值 (g)

// ============================================================
// 数据上报类
// ============================================================
class DataReporter {
public:
    /**
     * @brief 构造函数
     * @param protocol BM53协议对象指针
     * @param servo 舵机控制器指针
     */
    DataReporter(BM53_Protocol* protocol, ServoController* servo);

    /**
     * @brief 初始化上报模块
     */
    void begin();

    /**
     * @brief 主循环调用，根据间隔自动上报各类数据
     * @param forceValues 压力值数组
     * @param isTherapyActive 治疗是否激活
     * @param motorState 电机状态
     * @param therapyStartTime 治疗开始时间
     * @param therapyEndTime 治疗结束时间
     * @param therapyDuration 治疗总时长（分钟）
     * @param bioCalSuccess 生物阻抗校准是否成功
     * @param z20k 20kHz阻抗值
     * @param z50k 50kHz阻抗值
     */
    void update(long* forceValues, 
                bool isTherapyActive,
                bool motorState,
                unsigned long therapyStartTime,
                unsigned long therapyEndTime,
                uint16_t therapyDuration,
                bool bioCalSuccess,
                double z20k,
                double z50k);

    /**
     * @brief 强制上报所有数据（治疗开始时调用）
     */
    void forceReportAll();

    /**
     * @brief 通知压力数据有变化
     */
    void notifyPressureChanged() { _pressureDataChanged = true; }

    /**
     * @brief 通知电机状态有变化
     */
    void notifyMotorChanged() { _motorStatusChanged = true; }

    /**
     * @brief 获取上报统计信息
     */
    unsigned long getTotalReports() const { return _totalReports; }

private:
    BM53_Protocol* _protocol;       // BM53协议对象
    ServoController* _servo;        // 舵机控制器

    // 上次上报时间戳
    unsigned long _lastPressureReport;
    unsigned long _lastEdemaReport;
    unsigned long _lastProgressReport;
    unsigned long _lastMotorReport;

    // 数据变化标志
    bool _pressureDataChanged;
    bool _motorStatusChanged;

    // 上次上报的压力值（用于变化检测）
    long _lastReportedForce[USE_CHANNELS];
    bool _forceWasNonZero[USE_CHANNELS];

    // 统计
    unsigned long _totalReports;

    // 内部上报函数
    void _reportAllPressureData(long* forceValues);
    void _reportEdemaData(bool bioCalSuccess, double z20k, double z50k);
    void _reportProgress(bool isTherapyActive, 
                         unsigned long therapyStartTime,
                         unsigned long therapyEndTime,
                         uint16_t therapyDuration);
    void _reportMotorStatus(bool motorState);
};

#endif // DATA_REPORTER_H