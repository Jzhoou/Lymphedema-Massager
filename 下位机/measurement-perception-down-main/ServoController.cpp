#include "ServoController.h"

// 构造函数
ServoController::ServoController(uint8_t i2cAddress)
  : pwm(Adafruit_PWMServoDriver(i2cAddress)),
    channels(nullptr),
    count(0) {
  // 初始化PID变量（每个通道独立参数，与参考版本一致）
  for (int i = 0; i < PID_CHANNEL_COUNT; i++) {
    currentPWM[i] = PID_INIT_PWM;
    lastError[i] = 0;
    integral[i] = 0;
  }
  // 六个通道独立的PID参数（从PID调试项目获取）
  targetForce[0] = 15.0; kp[0] = 1.0; ki[0] = 0.0; kd[0] = 0.0;
  targetForce[1] = 20.0; kp[1] = 0.35; ki[1] = 0.0; kd[1] = 0.0;
  targetForce[2] = 20.0; kp[2] = 0.5; ki[2] = 0.0; kd[2] = 0.0;
  targetForce[3] = 20.0; kp[3] = 0.4; ki[3] = 0.0; kd[3] = 0.0;
  targetForce[4] = 7.0;  kp[4] = 1.5; ki[4] = 0.0; kd[4] = 0.0;
  targetForce[5] = 20.0; kp[5] = 0.3; ki[5] = 0.0; kd[5] = 0.0;
}

// 初始化 PCA9685
void ServoController::begin() {
  pwm.begin();
  pwm.setPWMFreq(SERVO_FREQ);
  delay(10);
}

// 设置要控制的舵机通道
void ServoController::setChannels(const uint8_t* servoChannels, uint8_t servoCount) {
  channels = servoChannels;
  count = servoCount;
}

// 单个舵机控制
void ServoController::setPulse(uint8_t channel, uint16_t pulseValue) {
  pulseValue = constrain(pulseValue, PULSE_MIN, PULSE_MAX);
  pwm.setPWM(channel, 0, pulseValue);
}

// 控制所有已设置的舵机
void ServoController::setAllServos(uint16_t pulseValue) {
  pulseValue = constrain(pulseValue, PULSE_MIN, PULSE_MAX);
  for (uint8_t i = 0; i < count; i++) {
    pwm.setPWM(channels[i], 0, pulseValue);
  }
}

// 新增:使用PID控制设置所有舵机
void ServoController::setAllServosWithPID(uint16_t initialPWM) {
  for (uint8_t i = 0; i < count; i++) {
    currentPWM[i] = initialPWM;
    lastError[i] = 0;
    integral[i] = 0;
    pwm.setPWM(channels[i], 0, initialPWM);
  }
}

// PID计算函数（使用当前通道的独立PID参数）
uint16_t ServoController::calculatePID(float currentForce, uint8_t channel) {
  if (channel >= PID_CHANNEL_COUNT) return currentPWM[channel];

  // 使用当前通道独立的目标压力计算误差
  float error = targetForce[channel] - currentForce;
  
  // 积分项（带抗饱和）
  integral[channel] += error;
  // 积分限幅，防止积分饱和
  if (integral[channel] > 100.0f) integral[channel] = 100.0f;
  if (integral[channel] < -100.0f) integral[channel] = -100.0f;
  
  // 微分项
  float derivative = error - lastError[channel];
  
  // PID输出计算（使用当前通道独立的PID参数）
  float pidCorrection = kp[channel] * error 
                      + ki[channel] * integral[channel] 
                      + kd[channel] * derivative;
  
  // 更新PWM值 (注意:error为正时需要减小PWM让舵机伸直)
  float newPWM = currentPWM[channel] - pidCorrection;
  
  // 限制输出范围
  newPWM = constrain(newPWM, PID_OUTPUT_MIN, PID_OUTPUT_MAX);
  
  // 保存当前误差
  lastError[channel] = error;
  
  return (uint16_t)newPWM;
}

// 新增:根据压力反馈更新PID控制
void ServoController::updatePIDControl(long* forceValues, uint8_t channelCount) {
  for (uint8_t i = 0; i < channelCount && i < count; i++) {
    float currentForce = (float)forceValues[i];
    
    // 计算新的PWM值
    uint16_t newPWM = calculatePID(currentForce, i);
    
    // 只有PWM值变化才更新
    if (newPWM != currentPWM[i]) {
      currentPWM[i] = newPWM;
      pwm.setPWM(channels[i], 0, newPWM);
    }
  }
}

// 角度转 PWM（仅用于调试参考，注释标注近似角度）
uint16_t ServoController::angle2pwm(uint16_t angle) {
  angle = constrain(angle, 0, 270);
  return map(angle, 0, 270, PULSE_MIN, PULSE_MAX);
}

// ========== PID参数设置方法 ==========

// 设置所有通道的PID参数和目标压力
void ServoController::setPIDParams(float _kp, float _ki, float _kd, float _targetForce) {
  for (uint8_t i = 0; i < PID_CHANNEL_COUNT; i++) {
    kp[i] = _kp;
    ki[i] = _ki;
    kd[i] = _kd;
    targetForce[i] = _targetForce;
  }
}

// 设置单个通道的PID参数和目标压力
void ServoController::setPIDParams(uint8_t channel, float _kp, float _ki, float _kd, float _targetForce) {
  if (channel >= PID_CHANNEL_COUNT) return;
  kp[channel] = _kp;
  ki[channel] = _ki;
  kd[channel] = _kd;
  targetForce[channel] = _targetForce;
}

// 设置所有通道的目标压力
void ServoController::setTargetForce(float _targetForce) {
  for (uint8_t i = 0; i < PID_CHANNEL_COUNT; i++) {
    targetForce[i] = _targetForce;
  }
}

// 设置单个通道的目标压力
void ServoController::setTargetForce(uint8_t channel, float _targetForce) {
  if (channel >= PID_CHANNEL_COUNT) return;
  targetForce[channel] = _targetForce;
}
