#ifndef SERVO_CONTROLLER_H
#define SERVO_CONTROLLER_H

#include <Arduino.h>
#include <Wire.h>
#include <Adafruit_PWMServoDriver.h>

// 配置参数
#define PULSE_MIN 102   // 最小 PWM 值 (≈0°)
#define PULSE_MAX 512   // 最大 PWM 值 (≈270°)
#define SERVO_FREQ 50   // PWM 频率

// PID参数配置（默认值，每个通道可独立覆盖）
#define PID_TARGET_FORCE 25.0   // 默认目标压力 (g)
#define PID_KP 1.5              // 默认比例系数
#define PID_KI 0.0              // 默认积分系数
#define PID_KD 0.0              // 默认微分系数
#define PID_OUTPUT_MIN 200      // PID输出最小值(伸直)
#define PID_OUTPUT_MAX 275      // PID输出最大值(缩短)
#define PID_INIT_PWM 237        // PID初始PWM值
#define PID_CHANNEL_COUNT 6     // PID控制通道数

// 定义舵机通道
const uint8_t servoChannels[] = {0, 1, 2, 3, 4, 5};
const uint8_t servoCount = sizeof(servoChannels) / sizeof(servoChannels[0]);

class ServoController {
private:
  Adafruit_PWMServoDriver pwm;
  const uint8_t* channels;  // 舵机通道数组
  uint8_t count;            // 舵机数量
  
  // PID控制相关变量(每个通道独立)
  uint16_t currentPWM[PID_CHANNEL_COUNT];   // 当前PWM值
  float lastError[PID_CHANNEL_COUNT];       // 上次误差
  float integral[PID_CHANNEL_COUNT];        // 积分累积

  // 每个通道独立的PID参数
  float targetForce[PID_CHANNEL_COUNT];     // 每个通道的目标压力(g)
  float kp[PID_CHANNEL_COUNT];              // 每个通道的比例系数
  float ki[PID_CHANNEL_COUNT];              // 每个通道的积分系数
  float kd[PID_CHANNEL_COUNT];              // 每个通道的微分系数

  // PID计算函数
  uint16_t calculatePID(float currentForce, uint8_t channel);

public:
  // 构造函数
  ServoController(uint8_t i2cAddress = 0x40);

  // 初始化
  void begin();

  // 设置控制的舵机通道
  void setChannels(const uint8_t* servoChannels, uint8_t servoCount);

  // 核心控制函数 - 直接设置 PWM 值
  void setPulse(uint8_t channel, uint16_t pulseValue);

  // 批量控制
  void setAllServos(uint16_t pulseValue);
  
  // 新增:使用PID控制设置所有舵机
  void setAllServosWithPID(uint16_t initialPWM);
  
  // 新增:根据压力反馈更新PID控制
  void updatePIDControl(long* forceValues, uint8_t channelCount);

  // ========== PID参数设置方法 ==========

  /**
   * @brief 设置所有通道的PID参数和目标压力
   * @param _kp 比例系数
   * @param _ki 积分系数
   * @param _kd 微分系数
   * @param _targetForce 目标压力(g)
   */
  void setPIDParams(float _kp, float _ki, float _kd, float _targetForce);

  /**
   * @brief 设置单个通道的PID参数和目标压力
   * @param channel 通道索引(0-5)
   * @param _kp 比例系数
   * @param _ki 积分系数
   * @param _kd 微分系数
   * @param _targetForce 目标压力(g)
   */
  void setPIDParams(uint8_t channel, float _kp, float _ki, float _kd, float _targetForce);

  /**
   * @brief 设置所有通道的目标压力
   * @param _targetForce 目标压力(g)
   */
  void setTargetForce(float _targetForce);

  /**
   * @brief 设置单个通道的目标压力
   * @param channel 通道索引(0-5)
   * @param _targetForce 目标压力(g)
   */
  void setTargetForce(uint8_t channel, float _targetForce);

      /**
     * @brief 获取舵机当前PWM值
     * @param index 舵机索引（0-5）
     * @return PWM值
     */
    uint16_t getCurrentPWM(uint8_t index) const {
        if (index < 6) return currentPWM[index];
        return 0;
    }
    
    /**
     * @brief 将PWM值转换为角度（0-270°）
     * @param pwm PWM值
     * @return 角度值
     */
    float pwmToAngle(uint16_t pwm) const {
        return (float)(pwm - PULSE_MIN) * 270.0f / (float)(PULSE_MAX - PULSE_MIN);
    }
    
    /**
     * @brief 获取所有舵机的当前角度
     * @param angles 存储角度的数组（需至少6个元素）
     */
    void getCurrentAngles(float* angles) const {
        for (uint8_t i = 0; i < count && i < 6; i++) {
            angles[i] = pwmToAngle(currentPWM[i]);
        }
    }

  // 工具函数（调试用）
  uint16_t angle2pwm(uint16_t angle);
};

#endif