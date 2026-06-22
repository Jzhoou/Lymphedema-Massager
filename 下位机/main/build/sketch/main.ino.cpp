#include <Arduino.h>
#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
/*
 * 集成系统 - 使用治疗状态机的优化版本
 */

#include <Wire.h>
#include <Adafruit_GFX.h>
#include <Adafruit_SSD1306.h>
#include "ServoController.h"
#include "motor_control.h"
#include "MY2901.h"
#include "PressureSensor.h"
#include "AD5933.h"
#include "DisplayHelper.h"
#include "config.h"
#include "BM53_Protocol.h"
#include "DataReporter.h"
#include "TherapyStateMachine.h"  // 新增
#include "TimeStamp.h"            // ★ 时间戳模块

// ==================== 硬件对象创建 ====================
ServoController servo;
#define mySerial Serial4
MY2901 my2901(&mySerial);
PressureSensor pressureSensor;
Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire1, -1);
CalData cal20k, cal50k;

// 通信协议对象
BM53_Protocol bm53(Serial2);

// ★ 治疗状态机对象
TherapyStateMachine therapySM;

// ★ 数据上报对象
DataReporter reporter(&bm53, &servo);

// ==================== 治疗配置 ====================
TherapyConfig therapyConfig;

// ==================== 定时器变量 ====================
unsigned long systemStartTime = 0;
unsigned long lastSampleTime = 0;
unsigned long cycleStartTime = 0;

const unsigned long SAMPLE_INTERVAL = 200;
const unsigned long MOTOR_INTERVAL = 35000;


// ==================== 数据存储 ====================
uint16_t adValues[USE_CHANNELS];
long forceValues[USE_CHANNELS];
double Z20k, Phase20k;
double Z50k, Phase50k;
double bioRatio;

// ==================== 系统状态 ====================
bool motorState = false;
bool bioCalSuccess = false;
bool firstCycle = true;

// 连接状态监控
bool wasConnected = true;
unsigned long disconnectTime = 0;
const unsigned long DISCONNECT_TIMEOUT = 10000;

// ==================== 电机状态追踪 ====================
enum MotorPhase {
    MOTOR_IDLE,         // 空闲
    MOTOR_MOVING_TO_ARM, // 正在伸出
    MOTOR_MOVING_BACK   // 正在收回（到位后立即切换到下一个To_arm）
};

MotorPhase motorPhase = MOTOR_IDLE;
unsigned long lastMotorPositionCheck = 0;
const unsigned long MOTOR_POSITION_CHECK_INTERVAL = 100; // 100ms检查一次位置

// 目标位置定义（需要根据实际测量调整）
const long POSITION_BACK = 0;       // 收回位置（零点）
const long POSITION_ARM = 1616;     // 伸出位置（对应0x0650脉冲）
const long POSITION_TOLERANCE = 50; // 位置容差（脉冲数）

unsigned long pauseStartTimeForMotor = 0;   // 电机暂停开始时间
unsigned long totalPauseTimeForMotor = 0;   // 电机累计暂停时间

// 辅助函数：状态转字符串
#line 86 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
const char * stateToString(TherapyState state);
#line 102 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
bool isMotorAtPosition(long targetPosition);
#line 125 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
void onTherapyStateChange(TherapyState oldState, TherapyState newState);
#line 199 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
void onBm53Command(byte cmd, byte* data, uint16_t dataLen);
#line 420 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
void setup();
#line 578 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
void loop();
#line 86 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\main.ino"
const char* stateToString(TherapyState state) {
    switch (state) {
        case STATE_IDLE:            return "IDLE";
        case STATE_RUNNING:         return "RUNNING";
        case STATE_PAUSED:          return "PAUSED";
        case STATE_EMERGENCY_STOP:  return "EMERGENCY_STOP";
        case STATE_COMPLETED:       return "COMPLETED";
        default:                    return "UNKNOWN";
    }
}

/**
 * @brief  检查电机是否到达目标位置（使用缓存位置，非阻塞）
 * @param  targetPosition 目标位置
 * @return true:两个电机都到达目标位置
 */
bool isMotorAtPosition(long targetPosition) {
    // ★ 2号电机数据暂不可用，使用4号电机代替（两电机物理同步）
    long pos4 = motor_get_cached_position(0x04);
    
    // 检查4号电机位置是否有效
    if (pos4 < 0) {
        return false;  // 位置无效，可能还没读取到
    }
    
    // 检查4号电机是否在容差范围内
    return abs(pos4 - targetPosition) <= POSITION_TOLERANCE;
}

// ==================== 周期状态保存/恢复 ====================
struct CycleState {
    bool isToArmPhase;      // 当前是To_arm阶段还是back阶段
    bool motorMoving;       // 电机是否正在运动
    unsigned long cycleElapsedBeforePause; // 暂停前周期已用时间
};
CycleState savedCycleState;
bool hasSavedCycleState = false;

// ==================== 状态变化回调 ====================
void onTherapyStateChange(TherapyState oldState, TherapyState newState) {
    Serial.print(F("[状态机] "));
    Serial.print(stateToString(oldState));  // 旧状态
    Serial.print(F(" → "));
    Serial.print(stateToString(newState));  // 新状态
    Serial.println();
    
    switch (newState) {
        case STATE_RUNNING:
            Serial.println(F("RUNNING"));
            // 重置上报模块
            // 如果是从暂停恢复，不重置周期状态
            if (oldState == STATE_PAUSED) {
                // 暂停恢复：已在loop中处理断点续传
                Serial.println(F("[状态机] 从暂停恢复，从断点继续"));
                // 不重置 firstCycle 和 cycleStartTime
            }
            if (oldState == STATE_IDLE) {
                // 新治疗开始，重置所有计时
                firstCycle = true;
                motorPhase = MOTOR_IDLE;  // ★ 重置电机阶段
                hasSavedCycleState = false;  // ★ 清除保存的状态
                cycleStartTime = millis();
                lastSampleTime = millis();
                totalPauseTimeForMotor = 0;
                pauseStartTimeForMotor = 0;
                // ★ 重置断连追踪，防止之前的断连状态误触发自动停止
                disconnectTime = millis();
                wasConnected = true;
            }
            if (oldState == STATE_EMERGENCY_STOP) {
                // 从急停恢复，重新开始
                firstCycle = true;
                motorPhase = MOTOR_IDLE;
                hasSavedCycleState = false;
                cycleStartTime = millis();
                lastSampleTime = millis();
                totalPauseTimeForMotor = 0;
                pauseStartTimeForMotor = 0;
            }
            reporter.forceReportAll();
            break;
            
        case STATE_PAUSED:
           // 暂停时记录时间
            if (pauseStartTimeForMotor == 0) {
                pauseStartTimeForMotor = millis();
            }
            Serial.println(F("PAUSED"));
            motor_stop_all();
            servo.setAllServos(275);
            break;
            
        case STATE_IDLE:
            Serial.println(F("IDLE"));
            motor_stop_all();
            servo.setAllServos(275);
            break;
            
        case STATE_EMERGENCY_STOP:
            Serial.println(F("EMERGENCY_STOP"));
            motor_stop_all();
            servo.setAllServos(275);
            break;
            
        case STATE_COMPLETED:
            Serial.println(F("COMPLETED"));
            motor_stop_all();
            servo.setAllServos(275);
            break;
    }
}

// ==================== 命令回调函数 ====================
void onBm53Command(byte cmd, byte* data, uint16_t dataLen) {
    switch (cmd) {
        case BM53_CMD_HANDSHAKE:
            bm53.sendAck();
            Serial.println(F("[BM53] 握手响应"));
            break;
            
        case BM53_CMD_DEV_INFO:
            bm53.sendDeviceInfo("RehabDevice01", "v1.0.0");
            Serial.println(F("[BM53] 设备信息响应"));
            break;
            
        // ========== 开始治疗 ==========
        case BM53_CMD_START_THERAPY: {
            if (!therapySM.start(therapyConfig)) {
                bm53.sendResponse(BM53_CMD_START_THERAPY, 1, 0x01);
                Serial.println(F("[BM53] 开始治疗被拒绝: 急停状态"));
            } else {
                bm53.sendResponse(BM53_CMD_START_THERAPY, 0, 0);
                Serial.print(F("[BM53] 治疗开始 - 时长:"));
                Serial.print(therapyConfig.duration);
                Serial.println(F("分钟"));
            }
            break;
        }
        
        // ========== 暂停治疗 ==========
        case BM53_CMD_PAUSE_THERAPY: {
            if (therapySM.pause()) {
                bm53.sendResponse(BM53_CMD_PAUSE_THERAPY, 0, 0);
                Serial.println(F("[BM53] 治疗暂停"));
            } else {
                bm53.sendResponse(BM53_CMD_PAUSE_THERAPY, 1, 0x02);
                Serial.println(F("[BM53] 暂停被拒绝: 非运行状态"));
            }
            break;
        }
        
        // ========== 继续治疗 ==========
        case BM53_CMD_RESUME_THERAPY: {
            if (therapySM.resume()) {
                bm53.sendResponse(BM53_CMD_RESUME_THERAPY, 0, 0);
                Serial.println(F("[BM53] 治疗继续"));
            } else {
                bm53.sendResponse(BM53_CMD_RESUME_THERAPY, 1, 0x03);
                Serial.println(F("[BM53] 继续被拒绝: 非暂停状态"));
            }
            break;
        }
            
        // ========== 停止治疗 ==========
        case BM53_CMD_STOP_THERAPY: {
            therapySM.stop();
            bm53.sendResponse(BM53_CMD_STOP_THERAPY, 0, 0);
            Serial.println(F("[BM53] 治疗停止"));
            break;
        }
            
        // ========== 紧急停止 ==========
        case BM53_CMD_EME_STOP: {
            therapySM.emergencyStop();
            bm53.sendAck();
            Serial.println(F("[BM53] !!! 紧急停止 !!!"));
            break;
        }
        
        // ========== 退出急停状态 ==========
        case BM53_CMD_EXIT_EME_STOP: {
            if (therapySM.exitEmergencyStop()) {
                bm53.sendResponse(BM53_CMD_EXIT_EME_STOP, 0, 0);
                Serial.println(F("[BM53] 退出急停状态"));
            } else {
                bm53.sendResponse(BM53_CMD_EXIT_EME_STOP, 1, 0x04);
                Serial.println(F("[BM53] 退出急停被拒绝: 非急停状态"));
            }
            break;
        }
            
        // ========== 设置模式 ==========
        case BM53_CMD_SET_MODE:
            if (dataLen >= 1) {
                therapySM.setMode(data[0]);
                Serial.print(F("[BM53] 设置模式: "));
                Serial.println(data[0]);
                bm53.sendResponse(BM53_CMD_SET_MODE, 0, 0);
            } else {
                bm53.sendNak();
            }
            break;
            
        // ========== 设置强度 ==========
        case BM53_CMD_SET_INTENSITY:
            if (dataLen >= 1) {
                therapySM.setIntensity(data[0]);
                Serial.print(F("[BM53] 设置强度: "));
                Serial.print(data[0]);
                Serial.println(F("%"));
                bm53.sendResponse(BM53_CMD_SET_INTENSITY, 0, 0);
            } else {
                bm53.sendNak();
            }
            break;
            
        // ========== 设置时长 ==========
        case BM53_CMD_SET_DURATION:
            if (dataLen >= 1) {
                therapySM.setDuration(data[0]);
                Serial.print(F("[BM53] 设置时长: "));
                Serial.print(data[0]);
                Serial.println(F("分钟"));
                bm53.sendResponse(BM53_CMD_SET_DURATION, 0, 0);
            } else {
                bm53.sendNak();
            }
            break;
        
        // ========== 电机回零 ==========
        case BM53_CMD_MOTOR_HOME: {
            Serial.println(F("[BM53] 执行电机回零..."));
            motor_homing();
            bm53.sendResponse(BM53_CMD_MOTOR_HOME, 0, 0);
            Serial.println(F("[BM53] 电机回零完成"));
            break;
        }
        
                // ==========  电机步进（双电机同步） ==========
        case BM53_CMD_MOTOR_STEP: {
            // 数据格式: [方向(1)] [步数(4)] [速度(2)]
            // 方向: 0x00=伸出(CW), 0x01=收回(CCW)
            if (dataLen >= 7) {
                byte direction = data[0];
                unsigned long steps = ((unsigned long)data[1] << 24) | 
                                    ((unsigned long)data[2] << 16) | 
                                    ((unsigned long)data[3] << 8) | 
                                    (unsigned long)data[4];
                word speed = (data[5] << 8) | data[6];
                
                // 使用双电机同步步进
                motor_step_sync(direction, steps, speed);
                bm53.sendResponse(BM53_CMD_MOTOR_STEP, 0, 0);
                
                Serial.print(F("[BM53] 双电机同步步进 方向="));
                Serial.print(direction == 0x00 ? F("伸出") : F("收回"));
                Serial.print(F(" 步数="));
                Serial.print(steps);
                Serial.print(F(" 速度="));
                Serial.println(speed);
            } else {
                bm53.sendNak();
            }
            break;
        }
        
        // ========== 读取电机位置 ==========
        case BM53_CMD_MOTOR_POSITION: {
            // 数据格式: [电机ID(1)]
            byte motorId = (dataLen >= 1) ? data[0] : 0x02;
            
            // ★ 2号电机数据暂不可用，所有请求都使用4号电机
            byte actualMotorId = (motorId == 0x02) ? 0x04 : motorId;
            
            // 使用缓存位置，不阻塞
            long position = motor_get_cached_position(actualMotorId);
            if (position < 0) {
                // 缓存无效，降级为同步读取
                position = motor_read_position(actualMotorId);
            }
            
            // 返回位置数据
            byte posData[4];
            posData[0] = (byte)(position >> 24);
            posData[1] = (byte)(position >> 16);
            posData[2] = (byte)(position >> 8);
            posData[3] = (byte)(position);
            
            bm53.sendFrame(BM53_CMD_MOTOR_POSITION, posData, 4);
            
            Serial.print(F("[BM53] 电机位置 ID="));
            Serial.print(motorId, HEX);
            if (motorId == 0x02) {
                Serial.print(F("(实际查4号)"));
            }
            Serial.print(F(" 位置="));
            Serial.println(position);
            break;
        }
            
        case BM53_CMD_STEPPER_CTRL:
            bm53.sendAck();
            break;
            
        case BM53_CMD_SERVO_CTRL:
        case BM53_CMD_SERVO_HOME:
            bm53.sendAck();
            break;
            
        // ========== ★ 设置系统时间（上位机发NTP时间） ==========
        case BM53_CMD_SET_TIME: {
            // 数据格式: [unixTimeMs(8)] 小端序
            if (dataLen >= 8) {
                unsigned long long unixTimeMs = 0;
                for (uint8_t i = 0; i < 8; i++) {
                    unixTimeMs |= ((unsigned long long)data[i] << (i * 8));
                }
                systemTime.syncTime(unixTimeMs);
                bm53.sendResponse(BM53_CMD_SET_TIME, 0, 0);
            } else {
                bm53.sendNak();
            }
            break;
        }
        
        default:
            Serial.print(F("[BM53] 未知命令: 0x"));
            Serial.println(cmd, HEX);
            bm53.sendNak();
            break;
    }
}

// ==================== Setup ====================
void setup() {
    Serial.begin(115200);
    delay(1000);
    Serial.println(F("========== 系统初始化 =========="));
    
    // ---------- 1. 初始化 I2C 总线 ----------
    Wire1.begin();
    Wire1.setClock(100000);
    Wire.begin();
    delay(100);
    
    // ---------- 2. 初始化步进电机 ----------
    motor_init();
    Serial.println(F("[OK] 步进电机"));
        
    // ---------- 3. 初始化舵机 ----------
    servo.begin();
    servo.setChannels(servoChannels, servoCount);
    servo.setAllServos(275);
    Serial.println(F("[OK] 舵机"));
    
    // ---------- 4. 初始化压力传感器 ----------
    mySerial.begin(MY2901_BAUD_RATE);
    my2901.begin();
    pressureSensor.begin();
    Serial.println(F("[OK] 压力传感器"));
    
    // ---------- 5. 初始化 OLED ----------
    if (!display.begin(SSD1306_SWITCHCAPVCC, OLED_ADDR)) {
        Serial.println(F("[ERROR] OLED 初始化失败!"));
    }
    DisplayHelper::init(&display);
    Serial.println(F("[OK] OLED"));
    
    // ---------- 6. 初始化 AD5933 ----------
    if (!AD5933::initialize()) {
        Serial.println(F("[ERROR] AD5933 初始化失败"));
    }
    Serial.println(F("[OK] AD5933"));
    delay(200);
    
    // ---------- 7. 初始化通信协议 ----------
    bm53.setDebug(true);
    bm53.setDebugStream(&Serial);
    bm53.begin(460800);
    bm53.onCommand(onBm53Command);
    Serial.println(F("[OK] BM53 通信协议"));
    
    // ---------- 8. ★ 初始化治疗状态机 ----------
    therapySM.begin();
    therapySM.onStateChange(onTherapyStateChange);
    therapySM.setDisconnectTimeout(DISCONNECT_TIMEOUT);
    Serial.println(F("[OK] 治疗状态机"));
    
    // ---------- 9. ★ 初始化数据上报模块 ----------
    reporter.begin();
    Serial.println(F("[OK] 数据上报模块"));
    
    // ---------- 9.5. ★ 初始化时间戳模块 ----------
    systemTime.begin();
    Serial.println(F("[OK] 时间戳模块"));

    // ---------- 10. 电机回零 ----------
    Serial.println(F("[INFO] 开始电机回零..."));
    motor_homing();
    delay(10);
    Serial.println(F("[OK] 电机回零完成"));
    /*
    // ★ 加大到位窗口到 2° (原来是0.1°，太小了)
    {
        // 根据说明书 6.3.5 "修改驱动配置参数" 命令 0x48 0xD1
        // 最后两个字节 0x00 0x14 = 20，即 2.0°（单位0.1°）
        byte windowCmd[] = {
            0x00,                   // 广播地址
            0x48, 0xD1,             // 修改驱动配置参数
            0x01,                   // 保存到芯片
            // 下面21个字节是完整的驱动配置参数，只改最后两字节（到位窗口）
            0x19,                   // 电机类型 (1.8°)
            0x02,                   // 脉冲端口模式 (PUL_FOC)
            0x02,                   // 通讯端口复用 (UART_FUN)
            0x02,                   // En引脚有效电平 (Hold)
            0x00,                   // Dir有效方向 (CW)
            0x10,                   // 细分 (16)
            0x01,                   // 细分插补 (Enable)
            0x00,                   // 自动熄屏 (Disable)
            0x03, 0xE8,             // 开环工作电流 (1000mA)
            0x0B, 0xB8,             // 闭环堵转最大电流 (3000mA)
            0x0F, 0xA0,             // 闭环最大输出电压 (4000mV)
            0x05,                   // 串口波特率 (115200)
            0x07,                   // CAN通讯速率 (500000)
            0x01,                   // ID地址 (不变)
            0x00,                   // 通讯校验方式 (0x6B)
            0x01,                   // 控制命令应答 (Receive)
            0x01,                   // 堵转保护 (Enable)
            0x00, 0x28,             // 堵转保护转速阈值 (40RPM)
            0x09, 0x60,             // 堵转保护电流阈值 (2400mA)
            0x0F, 0xA0,             // 堵转保护检测时间 (4000ms)
            0x00, 0x14,             // ★ 到位窗口 = 20 (即2.0°)
            0x6B                    // 校验
        };
        MOTOR_SERIAL.write(windowCmd, sizeof(windowCmd));
        delay(100);
        Serial.println(F("[OK] 到位窗口已设为 2.0°"));
        
    }
    */
    // ---------- 11. 校准生物电阻抗 ----------
#if USE_CAL_MACROS
    cal20k.gain = CAL_GAIN_20KHZ;
    cal20k.phase = CAL_PHASE_20KHZ;
    cal50k.gain = CAL_GAIN_50KHZ;
    cal50k.phase = CAL_PHASE_50KHZ;
    bioCalSuccess = true;
    Serial.println(F("========== 使用宏定义校准系数 =========="));
    DisplayHelper::showCalibrationSuccess();
    delay(1500);
#else
    Serial.println(F("========== 校准中... =========="));
    DisplayHelper::showCalibrationStart(REF_RESIST);
    
    DisplayHelper::updateCalibrationProgress("20kHz...");
    if (!AD5933::calibrateFrequency(FREQ_20KHZ, REF_RESIST, CAL_SAMPLES, cal20k)) {
        DisplayHelper::showCalibrationFailed("20kHz");
        Serial.println(F("[ERROR] 20kHz 校准失败"));
        for (;;);
    }
    
    DisplayHelper::updateCalibrationProgress("50kHz...");
    if (!AD5933::calibrateFrequency(FREQ_50KHZ, REF_RESIST, CAL_SAMPLES, cal50k)) {
        DisplayHelper::showCalibrationFailed("50kHz");
        Serial.println(F("[ERROR] 50kHz 校准失败"));
        for (;;);
    }
    
    bioCalSuccess = true;
    DisplayHelper::showCalibrationSuccess();
    Serial.println(F("[OK] 校准完成"));
    delay(1500);
#endif
    
    // ---------- 12. 初始化定时器 ----------
    systemStartTime = millis();
    lastSampleTime = systemStartTime;
    cycleStartTime = systemStartTime;
    
    display.clearDisplay();
    display.setTextSize(1);
    display.setCursor(0, 20);
    display.println(F("System Ready"));
    display.setCursor(0, 35);
    display.println(F("Waiting for cmd..."));
    display.display();
    
    Serial.println(F("========== 系统启动 =========="));
    Serial.println(F("等待上位机指令..."));
}

// ==================== Loop ====================
void loop() {
    unsigned long currentTime = millis();
    
    // ========== 最高优先级：通信处理 ==========
    bm53.loop();
    
    // ========== 电机位置异步轮询（每循环调用一次） ==========
    motor_poll_positions();  // 自动轮流读取两个电机位置

    // ==========  更新治疗状态机 ==========
    therapySM.update();
    
    // ==========  更新连接状态 ==========
    bool currentlyConnected = bm53.isConnected();
    
    if (wasConnected && !currentlyConnected) {
        disconnectTime = currentTime;
    }
    
    unsigned long disconnectedDuration = currentlyConnected ? 0 : (currentTime - disconnectTime);
    therapySM.updateConnectionStatus(currentlyConnected, disconnectedDuration);
    
    wasConnected = currentlyConnected;
    
    // ========== 急停检查 ==========
    if (therapySM.isEmergencyStopped()) {
        return;  // 急停状态下只处理通信，不执行任何动作
    }
    
    // ========== 治疗激活检查 ==========
    if (!therapySM.isTherapyActive()) {
        // 显示断连信息
        if (therapySM.isAutoStopDueToDisconnect() && currentlyConnected) {
            display.clearDisplay();
            display.setTextSize(1);
            display.setCursor(0, 20);
            display.println(F("Reconnected"));
            display.setCursor(0, 35);
            display.println(F("Send START to"));
            display.setCursor(0, 50);
            display.println(F("resume therapy"));
            display.display();
        }
        return;
    }
    
    unsigned long adjustedCurrentTime = currentTime - totalPauseTimeForMotor;


    // ========== 暂停状态处理 ==========
    if (therapySM.isPaused()) {
        // 暂停时保存状态
        if (!hasSavedCycleState) {
            savedCycleState.isToArmPhase = motorState;
            savedCycleState.motorMoving = (motorPhase == MOTOR_MOVING_TO_ARM || motorPhase == MOTOR_MOVING_BACK);
            savedCycleState.cycleElapsedBeforePause = (adjustedCurrentTime - cycleStartTime);
            hasSavedCycleState = true;
            Serial.println(F("[暂停] 周期状态已保存"));
            Serial.print(F("  isToArmPhase=")); Serial.println(savedCycleState.isToArmPhase);
            Serial.print(F("  motorMoving=")); Serial.println(savedCycleState.motorMoving);
            Serial.print(F("  cycleElapsed=")); Serial.print(savedCycleState.cycleElapsedBeforePause / 1000); Serial.println(F("秒"));
        }
        // 暂停状态下继续上报但不执行动作
        reporter.update(forceValues, 
                        therapySM.isTherapyActive(),
                        motorState,
                        therapySM.getStartTime(),
                        therapySM.getEndTime(),
                        therapySM.getConfig().duration,
                        bioCalSuccess,
                        Z20k, Z50k);
        return;
    }
    else {
        // 恢复时加载状态
        if (hasSavedCycleState && pauseStartTimeForMotor > 0) {
            totalPauseTimeForMotor += (currentTime - pauseStartTimeForMotor);
            pauseStartTimeForMotor = 0;
            
            // 重新计算 adjustedCurrentTime
            adjustedCurrentTime = currentTime - totalPauseTimeForMotor;

            // 恢复保存的周期状态
            motorState = savedCycleState.isToArmPhase;
            if (savedCycleState.motorMoving) {
                // 如果暂停时电机正在运动，需要重新发送命令
                if (motorState) {
                    Serial.println(F("[恢复] 重新执行To_arm"));
                    servo.setAllServos(200);
                    delay(100);
                    To_arm();
                    motor_notify_move_started(); // ★ 新增
                    motorPhase = MOTOR_MOVING_TO_ARM;
                } else {
                    Serial.println(F("[恢复] 重新执行back"));
                    servo.setAllServos(265);
                    delay(100);
                    back();
                    motor_notify_move_started(); // ★ 新增
                    motorPhase = MOTOR_MOVING_BACK;
                }
                 motor_invalidate_reached();  // ★ 改为清到位状态
            }
            else {
                // 暂停时电机已到位，根据方向恢复到对应的运动阶段
                // 由于已去掉AT_ARM/AT_BACK，到位后应立即触发下一阶段
                // 这里让cycleStartTime设为过去，使超时兜底立即生效，或直接启动下一阶段
                if (motorState) {
                    // 上次是To_arm阶段且已到位，立即开始back
                    Serial.println(F("[恢复] 到位状态，立即执行Back"));
                    servo.setAllServos(265);
                    delay(100);
                    back();
                    motor_notify_move_started();
                    motorPhase = MOTOR_MOVING_BACK;
                    motorState = false;
                } else {
                    // 上次是back阶段且已到位，立即开始新周期To_arm
                    Serial.println(F("[恢复] 到位状态，立即执行To_arm"));
                    servo.setAllServos(200);
                    delay(100);
                    To_arm();
                    motor_notify_move_started();
                    motorPhase = MOTOR_MOVING_TO_ARM;
                    motorState = true;
                }
                cycleStartTime = adjustedCurrentTime;
                motor_invalidate_reached();
            }

            hasSavedCycleState = false;
            
            Serial.print(F("[INFO] 治疗恢复，暂停时长: "));
            Serial.print(totalPauseTimeForMotor / 1000);
            Serial.println(F(" 秒"));
            Serial.print(F("[INFO] 从断点继续，当前阶段: "));
            Serial.print(motorState ? F("To_arm/AT_ARM") : F("Back/AT_BACK"));
            Serial.print(F(" motorPhase="));
            Serial.println(motorPhase);
        }

    }
    
    // ========== 治疗时间检查 ==========
    if (therapySM.isTimeUp() || therapySM.getState() == STATE_COMPLETED) {
        reporter.update(forceValues,
                        therapySM.isTherapyActive(),
                        motorState,
                        therapySM.getStartTime(),
                        therapySM.getEndTime(),
                        therapySM.getConfig().duration,
                        bioCalSuccess,
                        Z20k, Z50k);
        return;
    }
    

    // ========== 基于到位状态的电机状态机 ==========
    // 到位后立即切换到下一阶段，MOTOR_INTERVAL 仅作为超时兜底（到位检测失效时的保护）
    switch (motorPhase) {
        case MOTOR_IDLE:
            if (firstCycle || !motorState) {
                Serial.println(F("\n========== To_arm 阶段 =========="));
                servo.setAllServosWithPID(237);
                delay(100);
                To_arm();
                motor_notify_move_started();
                motorPhase = MOTOR_MOVING_TO_ARM;
                motorState = true;
                firstCycle = false;
                cycleStartTime = adjustedCurrentTime;  // ★ 记录运动开始时间（用于超时兜底）
                motor_invalidate_reached();
                reporter.notifyMotorChanged();
            }
            break;
            
        case MOTOR_MOVING_TO_ARM:
            // ★ 到位后立即切换到back阶段
            if (motor_both_reached()) {
                Serial.println(F("[到位] 已到达伸出位置，立即切换到Back"));
                motor_invalidate_reached();
                
                // 立即开始back
                Serial.println(F("\n========== Back 阶段 =========="));
                servo.setAllServos(265);
                delay(100);
                back();
                motor_notify_move_started();
                motorPhase = MOTOR_MOVING_BACK;
                motorState = false;
                cycleStartTime = adjustedCurrentTime;  // ★ 重置超时计时
                motor_invalidate_reached();
                reporter.notifyMotorChanged();
            }
            // ★ 超时兜底：到位检测失败时，超过MOTOR_INTERVAL强制切换
            else if (adjustedCurrentTime - cycleStartTime >= MOTOR_INTERVAL) {
                Serial.println(F("[超时] To_arm到位检测超时，强制切换到Back"));
                motor_invalidate_reached();
                
                Serial.println(F("\n========== Back 阶段(超时触发) =========="));
                servo.setAllServos(265);
                delay(100);
                back();
                motor_notify_move_started();
                motorPhase = MOTOR_MOVING_BACK;
                motorState = false;
                cycleStartTime = adjustedCurrentTime;
                motor_invalidate_reached();
                reporter.notifyMotorChanged();
            }
            break;
            
        case MOTOR_MOVING_BACK:
            // ★ 到位后立即切换到新的To_arm阶段
            if (motor_both_reached()) {
                Serial.println(F("[到位] 已到达收回位置，立即切换到To_arm"));
                motor_invalidate_reached();
                
                // 立即开始新周期To_arm
                Serial.println(F("\n========== 新周期 To_arm 阶段 =========="));
                servo.setAllServos(200);
                delay(100);
                To_arm();
                motor_notify_move_started();
                motorPhase = MOTOR_MOVING_TO_ARM;
                motorState = true;
                cycleStartTime = adjustedCurrentTime;  // ★ 重置超时计时
                motor_invalidate_reached();
                reporter.notifyMotorChanged();
            }
            // ★ 超时兜底：到位检测失败时，超过MOTOR_INTERVAL强制切换
            else if (adjustedCurrentTime - cycleStartTime >= MOTOR_INTERVAL) {
                Serial.println(F("[超时] Back到位检测超时，强制切换到To_arm"));
                motor_invalidate_reached();
                
                Serial.println(F("\n========== 新周期 To_arm 阶段(超时触发) =========="));
                servo.setAllServos(200);
                delay(100);
                To_arm();
                motor_notify_move_started();
                motorPhase = MOTOR_MOVING_TO_ARM;
                motorState = true;
                cycleStartTime = adjustedCurrentTime;
                motor_invalidate_reached();
                reporter.notifyMotorChanged();
            }
            break;
    }

    
    // ========== 压力采集 + PID控制（仅在To_arm阶段执行） ==========
    if (motorState) {
        if (adjustedCurrentTime - lastSampleTime >= SAMPLE_INTERVAL) {
            lastSampleTime = currentTime;
            
            if (my2901.update()) {
                my2901.getMultiAD(adValues, USE_CHANNELS);
                pressureSensor.batchConvert(adValues, forceValues, USE_CHANNELS);
                
                // ★ PID控制：根据压力反馈动态调整舵机
                servo.updatePIDControl(forceValues, USE_CHANNELS);
                
                for (uint8_t i = 0; i < USE_CHANNELS; i++) {
                    static long lastForce[USE_CHANNELS] = {0};
                    if (abs(forceValues[i] - lastForce[i]) >= FORCE_CHANGE_THRESHOLD) {
                        reporter.notifyPressureChanged();
                        break;
                    }
                    lastForce[i] = forceValues[i];
                }
                
                for (uint8_t i = 0; i < USE_CHANNELS; i++) {
                    Serial.print(forceValues[i]);
                    if (i < USE_CHANNELS - 1) Serial.print('\t');
                }
                Serial.println();
            }
        }
    } else {
        if (bioCalSuccess) {
            bool ok20k = AD5933::measureImpedance(FREQ_20KHZ, cal20k, MEAS_SAMPLES, Z20k, Phase20k);
            delay(50);
            bool ok50k = AD5933::measureImpedance(FREQ_50KHZ, cal50k, MEAS_SAMPLES, Z50k, Phase50k);
            
            if (ok20k && ok50k) {
                bioRatio = Z20k / Z50k;
                DisplayHelper::showMeasurementResult(Z20k, Z50k, bioRatio);
            }
        }
    }
    
    // ========== 数据上报 ==========
    reporter.update(forceValues,
                    therapySM.isTherapyActive(),
                    motorState,
                    therapySM.getStartTime(),
                    therapySM.getEndTime(),
                    therapySM.getConfig().duration,
                    bioCalSuccess,
                    Z20k, Z50k);
    
    // ========== 监控队列状态 ==========
    static unsigned long lastQueueCheck = 0;
    if (currentTime - lastQueueCheck >= 5000) {
        int queueCount = bm53.getQueueCount();
        if (queueCount > 10) {
            Serial.print(F("[警告] 发送队列积压: "));
            Serial.print(queueCount);
            Serial.println(F(" 个包"));
        }
        lastQueueCheck = currentTime;
    }
}
