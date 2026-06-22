#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\motor_control.cpp"
#include "motor_control.h"

/************************************************
 * 全局指令数组
 ************************************************/
// 电机同步运行指令：地址0 + 0xFF + 0x66 + 校验0x6B
// 作用：发送该指令后，之前设置好参数的电机同时启动
byte together_Motor[] = {0x00, 0xFF, 0x66, 0x6B};

/************************************************
 * ========== 异步位置读取相关变量 ==========
 ************************************************/
static bool _asyncRequestPending = false;     // 是否有未完成的异步请求
static byte _asyncMotorId = 0;                // 正在请求的电机ID
static unsigned long _asyncRequestTime = 0;   // 请求发送时间
static long _asyncPositionResult = -1;        // 异步读取结果
static bool _asyncResultReady = false;        // 结果是否就绪

// 异步请求队列（支持轮询两个电机）
static byte _asyncMotorQueue[2] = {0x02, 0x04};  // 电机ID队列
static uint8_t _asyncQueueIndex = 0;              // 当前队列索引
static long _asyncMotorPositions[2] = {-1, -1};   // 两个电机位置缓存
static bool _asyncMotorPositionValid[2] = {false, false}; // 位置是否有效
static unsigned long _asyncLastCompleteTime = 0;  // 上次完成时间

/************************************************
 * 内部函数声明
 ************************************************/
static void sendMotorCmd(byte ID, byte dir, byte v1, byte v2, byte a,
                         byte d1, byte d2, byte d3, byte d4, byte absolute);

/************************************************
 * ★ 位置数据缓存（静态全局变量）
 ************************************************/
static long _lastPosition = -1;
static bool _newPositionData = false;
static unsigned long _positionRequestTime = 0;


// ★ 新增：电机运动开始后的屏蔽时间（毫秒）
#define MOTOR_MOVE_BLIND_MS 200  // 运动开始后200ms内不上报到位

static unsigned long _motorMoveStartTime = 0;  // 运动开始时间
static bool _motorMoving = false;               // 电机是否在运动中

/************************************************
 * 函数实现
 ************************************************/

// ==================== 异步位置读取实现 ====================

/**
 * @brief  启动异步位置读取（非阻塞）
 * @param  motorId 电机ID (0x02 或 0x04)
 * @return true:请求已发送 false:上一个请求未完成
 */
bool motor_request_position_async(byte motorId) {
    if (_asyncRequestPending) {
        return false;  // 上一个请求还未完成
    }
    
    // 发送读取位置命令：ID + 0x36 + 校验
    byte cmd[] = {motorId, 0x36, MOTOR_FIXED_CHECKSUM};
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    
    _asyncRequestPending = true;
    _asyncMotorId = motorId;
    _asyncRequestTime = millis();
    _asyncResultReady = false;
    _asyncPositionResult = -1;
    
    return true;
}



/**
 * @brief  检查异步读取结果（非阻塞，在loop中调用）
 * @param  position 输出参数，存储读取到的位置
 * @return true:结果已就绪（成功或超时） false:仍在等待
 */
bool motor_get_position_async_result(long* position) {
    if (!_asyncRequestPending) {
        return false;
    }
    
    // ========== 超时检查 ==========
    if (millis() - _asyncRequestTime > 200) {  // 缩短超时到200ms
        _asyncRequestPending = false;
        _asyncResultReady = true;
        _asyncPositionResult = -1;
        if (position) *position = -1;
        return true;  // 超时也算完成，返回-1
    }
    
    // ========== ★ 响应帧为8字节：地址 + 0x36 + 符号 + 位置(4字节) + 校验 ==========
    if (MOTOR_SERIAL.available() < 8) {
        return false;  // 数据还不够
    }
    
    // ========== 读取响应 ==========
    byte response[8];
    int bytesRead = 0;
    
    while (MOTOR_SERIAL.available() && bytesRead < 8) {
        response[bytesRead++] = MOTOR_SERIAL.read();
    }
    
    // ========== 验证响应 ==========
    if (bytesRead >= 8) {
        // ★ 检查帧格式：地址 + 0x36 + 符号 + 位置(4) + 校验
        if (response[0] == _asyncMotorId && 
            response[1] == 0x36 && 
            response[7] == MOTOR_FIXED_CHECKSUM) {  // ★ 校验在第7字节（索引7）
            
            // ★ 解析位置数据（字节2是符号，字节3-6是位置，大端序）
            byte sign = response[2];  // 符号：0x00=正，0x01=负
            long rawPosition = ((long)response[3] << 24) | 
                               ((long)response[4] << 16) | 
                               ((long)response[5] << 8) | 
                               (long)response[6];
            
            // 如果有符号位且为负
            if (sign == 0x01) {
                _asyncPositionResult = -rawPosition;
            } else {
                _asyncPositionResult = rawPosition;
            }
        } else {
            _asyncPositionResult = -1;  // 校验失败
        }
    } else {
        _asyncPositionResult = -1;
    }
    
    // 清理请求状态
    _asyncRequestPending = false;
    _asyncResultReady = true;
    
    if (position) {
        *position = _asyncPositionResult;
    }
    
    return true;
}

/**
 * @brief  取消正在进行的异步请求
 */
void motor_cancel_async_request() {
    _asyncRequestPending = false;
    _asyncResultReady = false;
}

/**
 * @brief  启动异步状态查询（查询到位标志）
 */
bool motor_request_status_async(byte motorId) {
    if (_asyncRequestPending) {
        return false;
    }
    
    // 发送读取状态命令：ID + 0x3A + 校验
    byte cmd[] = {motorId, 0x3A, MOTOR_FIXED_CHECKSUM};
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    
    _asyncRequestPending = true;
    _asyncMotorId = motorId;
    _asyncRequestTime = millis();
    _asyncResultReady = false;
    _asyncPositionResult = -1;
    
    return true;
}

/**
 * @brief  检查状态查询结果
 * @param  reached 输出：是否到位
 * @return true:结果就绪 false:等待中
 */
bool motor_get_status_async_result(bool* reached) {
    if (!_asyncRequestPending) {
        return false;
    }
    
    // 超时检查
    if (millis() - _asyncRequestTime > 200) {
        _asyncRequestPending = false;
        _asyncResultReady = true;
        if (reached) *reached = false;
        return true;
    }
    
    if (MOTOR_SERIAL.available() < 4) {
        return false;
    }
    
    byte response[4];
    int bytesRead = 0;
    while (MOTOR_SERIAL.available() && bytesRead < 4) {
        response[bytesRead++] = MOTOR_SERIAL.read();
    }
    
    bool isReached = false;
    if (bytesRead >= 4) {
        if (response[0] == _asyncMotorId && 
            response[1] == 0x3A && 
            response[3] == MOTOR_FIXED_CHECKSUM) {
            
            byte status = response[2];
            isReached = (status & 0x02) != 0;
            
            // ★ 添加详细日志，每个电机只打印一次
            static byte lastStatus[2] = {0xFF, 0xFF};
            uint8_t idx = (_asyncMotorId == 0x02) ? 0 : 1;
            if (status != lastStatus[idx]) {
                Serial.print(F("[状态] 电机0x"));
                Serial.print(_asyncMotorId, HEX);
                Serial.print(F(" 原始状态=0x"));
                if (status < 0x10) Serial.print('0');
                Serial.print(status, HEX);
                Serial.print(F(" 使能="));
                Serial.print((status & 0x01) ? "是" : "否");
                Serial.print(F(" 到位="));
                Serial.print((status & 0x02) ? "是" : "否");
                Serial.print(F(" 堵转="));
                Serial.print((status & 0x04) ? "是" : "否");
                Serial.print(F(" 堵转保护="));
                Serial.println((status & 0x08) ? "是" : "否");
                lastStatus[idx] = status;
            }
        }
    }
    
    _asyncRequestPending = false;
    _asyncResultReady = true;
    
    if (reached) {
        *reached = isReached;
    }
    
    return true;
}

// ==================== 双电机轮询读取实现 ====================
// ========== 到位状态缓存 ==========
static bool _motorReached[2] = {false, false};
static bool _motorReachedValid[2] = {false, false};

void motor_invalidate_reached() {
    // ★ 只标记为待验证，不清除到位值
    _motorReachedValid[0] = false;
    _motorReachedValid[1] = false;
    // 不清除 _motorReached[0] 和 _motorReached[1] 的值
    
    _motorMoving = false;
    motor_cancel_async_request();  // ★ 新增：取消正在等待的poll请求，防止读到过时响应
    motor_clear_rx_buffer();       // 清空串口缓冲区，丢弃残留的旧数据
    Serial.println(F("[到位] 到位状态标记为待验证"));
}


bool motor_both_reached() {
    // ★ 运动开始后200ms内不检测到位（电机加速阶段）
    if (_motorMoving && (millis() - _motorMoveStartTime) < MOTOR_MOVE_BLIND_MS) {
        return false;
    }
    
    // ★ 如果到位状态无效，返回 false 等待重新验证
    if (!_motorReachedValid[1]) {
        return false;
    }
    
    // ★ 添加运动超时保护（例如10秒）
    if (_motorMoving && (millis() - _motorMoveStartTime) > 10000) {
        Serial.println(F("[到位] 运动超时，强制认为到位"));
        return true;
    }
    
    // 检查4号电机到位状态
    if (_motorReached[1]) {
        return true;
    }
    return false;
}

// ★ 新增：清空电机串口接收缓冲区
void motor_clear_rx_buffer() {
    while (MOTOR_SERIAL.available()) {
        MOTOR_SERIAL.read();
    }
}

// ★ 在发送运动指令后调用
void motor_notify_move_started() {
    _motorMoveStartTime = millis();
    _motorMoving = true;
    motor_cancel_async_request();  // ★ 新增：取消旧的异步请求，防止残留响应干扰
    motor_clear_rx_buffer();       // 清空旧数据
    Serial.println(F("[电机] 运动开始，清空缓冲并屏蔽到位检测200ms"));
}

// motor_poll_positions 里要查询状态0x3A
/*void motor_poll_positions() {
    if (_asyncRequestPending) {
        bool reached;
        if (motor_get_status_async_result(&reached)) {
            uint8_t idx = (_asyncMotorId == 0x02) ? 0 : 1;
            _motorReached[idx] = reached;
            _motorReachedValid[idx] = true;
            
            Serial.print(F("[轮询] 电机0x"));
            Serial.print(_asyncMotorId, HEX);
            Serial.print(F(" 到位="));
            Serial.println(reached ? "是" : "否");
            
            _asyncLastCompleteTime = millis();
            _asyncQueueIndex = (_asyncQueueIndex + 1) % 2;
        }
        return;
    }
    
    if (millis() - _asyncLastCompleteTime >= 50) {
        byte nextMotor = _asyncMotorQueue[_asyncQueueIndex];
        motor_request_status_async(nextMotor);
    }
}
*/
void motor_poll_positions() {
    if (_asyncRequestPending) {
        bool reached;
        if (motor_get_status_async_result(&reached)) {
            if (_asyncMotorId == 0x04) {
                // ★ 只在状态变化时打印
                static bool lastReached = false;
                if (reached != lastReached || !_motorReachedValid[1]) {
                    Serial.print(F("[轮询] 电机0x04 到位="));
                    Serial.println(reached ? "是" : "否");
                    lastReached = reached;
                }
                
                _motorReached[1] = reached;
                _motorReachedValid[1] = true;
            }
            
            _asyncLastCompleteTime = millis();
            // ★ 不要递增队列索引
        }
        return;
    }
    
    if (millis() - _asyncLastCompleteTime >= 100) {
        motor_request_status_async(0x04);
    }
}


bool motor_is_reached_valid() {
    return _motorReachedValid[1];  // 返回4号电机（索引1）的到位状态有效性
}

unsigned long motor_get_move_start_time() {
    return _motorMoveStartTime;
}

/**
 * @brief  获取缓存的到位状态
 */
bool motor_get_cached_reached(byte motorId) {
    // ★ 2号电机数据暂不可用，查询2号时返回4号电机状态
    uint8_t idx = (motorId == 0x02) ? 1 : 1;  // 全部使用4号电机（索引1）
    if (_motorReachedValid[idx]) {
        return _motorReached[idx];
    }
    return false;
}


/**
 * @brief  启动双电机位置轮询（在loop中调用，自动轮流读取两个电机）
 * @details 每次调用会尝试读取一个电机，读完自动切换下一个
 *          两个电机的位置会缓存到内部数组
 */
// ==================== 修改 motor_poll_positions 函数 ====================
/*void motor_poll_positions() {
    // ========== 如果当前有请求在处理中 ==========
    if (_asyncRequestPending) {
        long position;
        if (motor_get_position_async_result(&position)) {
            uint8_t idx = (_asyncMotorId == 0x02) ? 0 : 1;

            
            _asyncMotorPositions[idx] = position;
            _asyncMotorPositionValid[idx] = (position >= 0);
            
            Serial.print(F("[轮询] 电机0x"));
            Serial.print(_asyncMotorId, HEX);
            Serial.print(F(" 位置="));
            Serial.print(position);
            Serial.print(F(" 有效="));
            Serial.println(_asyncMotorPositionValid[idx] ? "是" : "否");
            
            _asyncLastCompleteTime = millis();
            _asyncQueueIndex = (_asyncQueueIndex + 1) % 2;
        }
        return;
    }
    
    // ========== 开始新请求 ==========
    if (millis() - _asyncLastCompleteTime >= 100) {
        byte nextMotor = _asyncMotorQueue[_asyncQueueIndex];
        motor_request_position_async(nextMotor);
    }
}*/

/**
 * @brief  获取缓存的电机位置（从轮询结果中读取）
 * @param  motorId 电机ID (0x02 或 0x04)
 * @return 位置值，-1表示无效或未读取
 */
long motor_get_cached_position(byte motorId) {
    // ★ 2号电机数据暂不可用，查询2号时返回4号电机位置
    uint8_t idx = 1;  // 全部使用4号电机（索引1）
    if (_asyncMotorPositionValid[idx]) {
        return _asyncMotorPositions[idx];
    }
    return -1;
}

/**
 * @brief  检查缓存的位置是否有效
 * @param  motorId 电机ID (0x02 或 0x04)
 * @return true:位置有效 false:无效或过期
 */
bool motor_is_position_valid(byte motorId) {
    // ★ 2号电机数据暂不可用，查询2号时返回4号电机有效性
    uint8_t idx = 1;  // 全部使用4号电机（索引1）
    return _asyncMotorPositionValid[idx];
}

/**
 * @brief  检查两个电机位置是否都有效
 * @return true:两个位置都有效
 */
bool motor_both_positions_valid() {
    // ★ 2号电机数据暂不可用，只需检查4号电机
    return _asyncMotorPositionValid[1];
}

/**
 * @brief  使缓存位置失效（强制下次重新读取）
 */
void motor_invalidate_positions() {
    _asyncMotorPositionValid[0] = false;
    _asyncMotorPositionValid[1] = false;
    _asyncMotorPositions[0] = -1;
    _asyncMotorPositions[1] = -1;
}

void motor_init(void)
{
    MOTOR_SERIAL.begin(MOTOR_BAUD_RATE);
    delay(1000);
}

void To_arm(void)
{
    // 速度30RPM(0x011E), 加速度60(0x3C), 脉冲0x000650(1616)
    sendMotorCmd(0x02, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x06, 0x50, 0x00, 0x01);
    sendMotorCmd(0x04, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x06, 0x50, 0x00, 0x01);
    MOTOR_SERIAL.write(together_Motor, sizeof(together_Motor));
}

void back(void)
{
    // 速度30RPM(0x011E), 加速度60(0x3C), 脉冲0x000000(回零位)
    sendMotorCmd(0x02, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x01);
    sendMotorCmd(0x04, 0x01, 0x01, 0x1E, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x01);
    MOTOR_SERIAL.write(together_Motor, sizeof(together_Motor));
}

void motor_stop_all(void)
{
    byte stopCmdAll[] = {0x00, 0xFE, 0x98, 0x00, 0x6B};
    MOTOR_SERIAL.write(stopCmdAll, sizeof(stopCmdAll));
}

static void sendMotorCmd(byte ID, byte dir, byte v1, byte v2, byte a,
                         byte d1, byte d2, byte d3, byte d4, byte absolute)
{
    byte cmd[13] = {
        ID,
        0xFD,
        dir,
        v1,
        v2,
        a,
        d1,
        d2,
        d3,
        d4,
        absolute,
        0x01,      // 多机同步运行标志 = 0x01（等待同步指令）
        MOTOR_FIXED_CHECKSUM
    };
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    delay(50);
}

static void splitSpeed(word speed, byte *v1, byte *v2)
{
    *v1 = highByte(speed);
    *v2 = lowByte(speed);
}

void motor_homing(void)
{
    Serial.begin(115200);
    Serial.println("\n========== 开始回零 ==========");
    
    pinMode(LIMIT_SWITCH_2_PIN, INPUT_PULLUP);
    pinMode(LIMIT_SWITCH_4_PIN, INPUT_PULLUP);
    delay(100);

    bool switch2_triggered = (digitalRead(LIMIT_SWITCH_2_PIN) == LOW);
    bool switch4_triggered = (digitalRead(LIMIT_SWITCH_4_PIN) == LOW);
    Serial.print("D4(2号电机限位)初始电平: "); Serial.println(switch2_triggered ? "LOW(已在零点)" : "HIGH(需要回零)");
    Serial.print("D5(4号电机限位)初始电平: "); Serial.println(switch4_triggered ? "LOW(已在零点)" : "HIGH(需要回零)");

    if (switch2_triggered) {
        Serial.println("2号电机: 已在零点，直接清零位置");
        byte cmd2_zero[] = {0x02, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
        MOTOR_SERIAL.write(cmd2_zero, sizeof(cmd2_zero));
        delay(50);
    }

    if (switch4_triggered) {
        Serial.println("4号电机: 已在零点，直接清零位置");
        byte cmd4_zero[] = {0x04, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
        MOTOR_SERIAL.write(cmd4_zero, sizeof(cmd4_zero));
        delay(50);
    }

    if (switch2_triggered && switch4_triggered) {
        Serial.println("========== 回零完成(全部已在零点) ==========\n");
        return;
    }

    word speed = HOMING_SPEED * 60;

    if (!switch2_triggered) {
        Serial.print("2号电机: 开始反转回零... ");
        byte v1, v2;
        splitSpeed(speed, &v1, &v2);
        byte cmd2_run[] = {
            0x02,
            0xF6,
            0x00,
            v1,
            v2,
            0x00,
            0x00,
            MOTOR_FIXED_CHECKSUM
        };
        MOTOR_SERIAL.write(cmd2_run, sizeof(cmd2_run));
        delay(50);
        Serial.println("速度命令已发送");
    }

    if (!switch4_triggered) {
        Serial.print("4号电机: 开始反转回零... ");
        byte v1, v2;
        splitSpeed(speed, &v1, &v2);
        byte cmd4_run[] = {
            0x04,
            0xF6,
            0x00,
            v1,
            v2,
            0x00,
            0x00,
            MOTOR_FIXED_CHECKSUM
        };
        MOTOR_SERIAL.write(cmd4_run, sizeof(cmd4_run));
        delay(50);
        Serial.println("速度命令已发送");
    }

    unsigned long startTime = millis();
    const unsigned long timeout = 30000;
    Serial.print("开始监测限位(超时时间: "); Serial.print(timeout); Serial.println("ms)...");

    while (true) {
        if (!switch2_triggered && digitalRead(LIMIT_SWITCH_2_PIN) == LOW) {
            Serial.println("2号电机: 限位触发，立即停止并清零位置");
            byte stop2[] = {0x02, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(stop2, sizeof(stop2));
            delay(10);
            switch2_triggered = true;
        }

        if (!switch4_triggered && digitalRead(LIMIT_SWITCH_4_PIN) == LOW) {
            Serial.println("4号电机: 限位触发，立即停止并清零位置");
            byte stop4[] = {0x04, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(stop4, sizeof(stop4));
            delay(10);
            switch4_triggered = true;
        }

        if (switch2_triggered && switch4_triggered) {
            byte cmd2_zero[] = {0x02, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(cmd2_zero, sizeof(cmd2_zero));
            delay(50);
            Serial.println("2号电机: 回零完成");
            byte cmd4_zero[] = {0x04, 0x0A, 0x6D, MOTOR_FIXED_CHECKSUM};
            MOTOR_SERIAL.write(cmd4_zero, sizeof(cmd4_zero));
            delay(50);
            Serial.println("4号电机: 回零完成");
            Serial.println("========== 全部回零完成 ==========\n");
            break;
        }

        if (millis() - startTime > timeout) {
            Serial.println("!!! 回零超时 !!! 强制停止");
            if (!switch2_triggered) {
                byte stop2[] = {0x02, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
                MOTOR_SERIAL.write(stop2, sizeof(stop2));
                delay(50);
            }
            if (!switch4_triggered) {
                byte stop4[] = {0x04, 0xFE, 0x98, 0x00, MOTOR_FIXED_CHECKSUM};
                MOTOR_SERIAL.write(stop4, sizeof(stop4));
                delay(50);
            }
            break;
        }

        delay(1);
    }
}

// ============================================================
// ★ 单电机步进（独立启动，不等待同步指令）
// ============================================================
void motor_step(byte motorId, byte direction, unsigned long steps, word speed) {
    byte v1 = highByte(speed);
    byte v2 = lowByte(speed);
    
    byte cmd[13] = {
        motorId,                    // [0] 电机地址
        0xFD,                       // [1] 位置控制指令
        direction,                  // [2] 方向
        v1,                         // [3] 速度高字节
        v2,                         // [4] 速度低字节
        0x8C,                       // [5] 加速度（默认值）
        (byte)(steps >> 24),        // [6] 脉冲数第1字节（最高字节）
        (byte)(steps >> 16),        // [7] 脉冲数第2字节
        (byte)(steps >> 8),         // [8] 脉冲数第3字节
        (byte)(steps),              // [9] 脉冲数第4字节（最低字节）
        0x01,                       // [10] 相对位置模式
        0x00,                       // [11] 立即执行 = 0x00（不等待同步信号，立即启动）
        MOTOR_FIXED_CHECKSUM        // [12] 校验
    };
    
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    delay(10);
}

// ============================================================
// ★ 双电机同步步进（推荐使用）
//    先分别设置两个电机的参数，然后发送同步启动指令
// ============================================================
void motor_step_sync(byte direction, unsigned long steps, word speed) {
    byte v1 = highByte(speed);
    byte v2 = lowByte(speed);
    byte d1 = (byte)(steps >> 24);
    byte d2 = (byte)(steps >> 16);
    byte d3 = (byte)(steps >> 8);
    byte d4 = (byte)(steps);
    
    // ===== 第1步：设置2号电机参数（同步标志 = 0x01，等待同步信号） =====
    byte cmd2[13] = {
        0x02,                       // [0] 电机地址：2号
        0xFD,                       // [1] 位置控制指令
        direction,                  // [2] 方向
        v1,                         // [3] 速度高字节
        v2,                         // [4] 速度低字节
        0x8C,                       // [5] 加速度
        d1, d2, d3, d4,             // [6-9] 脉冲数（4字节）
        0x01,                       // [10] 相对位置模式
        0x01,                       // [11] ★ 同步标志 = 0x01（等待同步信号）
        MOTOR_FIXED_CHECKSUM        // [12] 校验
    };
    MOTOR_SERIAL.write(cmd2, sizeof(cmd2));
    delay(50);  // 给电机模块处理时间
    
    // ===== 第2步：设置4号电机参数（同步标志 = 0x01，等待同步信号） =====
    byte cmd4[13] = {
        0x04,                       // [0] 电机地址：4号
        0xFD,                       // [1] 位置控制指令
        direction,                  // [2] 方向
        v1,                         // [3] 速度高字节
        v2,                         // [4] 速度低字节
        0x8C,                       // [5] 加速度
        d1, d2, d3, d4,             // [6-9] 脉冲数（4字节）
        0x01,                       // [10] 相对位置模式
        0x01,                       // [11] ★ 同步标志 = 0x01（等待同步信号）
        MOTOR_FIXED_CHECKSUM        // [12] 校验
    };
    MOTOR_SERIAL.write(cmd4, sizeof(cmd4));
    delay(50);
    
    // ===== 第3步：发送同步启动指令，两个电机同时启动 =====
    MOTOR_SERIAL.write(together_Motor, sizeof(together_Motor));
    
    Serial.print(F("[电机] 双电机同步步进: 方向="));
    Serial.print(direction == 0x00 ? F("伸出") : F("收回"));
    Serial.print(F(" 步数="));
    Serial.print(steps);
    Serial.print(F(" 速度="));
    Serial.print(speed);
    Serial.println(F("RPM"));
}

// ============================================================
// ★ 双电机异步步进（各自独立启动，不等待同步信号）
// ============================================================
void motor_step_async(byte direction, unsigned long steps, word speed) {
    byte v1 = highByte(speed);
    byte v2 = lowByte(speed);
    byte d1 = (byte)(steps >> 24);
    byte d2 = (byte)(steps >> 16);
    byte d3 = (byte)(steps >> 8);
    byte d4 = (byte)(steps);
    
    // ===== 2号电机：立即执行 =====
    byte cmd2[13] = {
        0x02, 0xFD, direction,
        v1, v2,
        0x8C,
        d1, d2, d3, d4,
        0x01,       // 相对位置模式
        0x00,       // ★ 立即执行
        MOTOR_FIXED_CHECKSUM
    };
    MOTOR_SERIAL.write(cmd2, sizeof(cmd2));
    delay(10);
    
    // ===== 4号电机：立即执行 =====
    byte cmd4[13] = {
        0x04, 0xFD, direction,
        v1, v2,
        0x8C,
        d1, d2, d3, d4,
        0x01,       // 相对位置模式
        0x00,       // ★ 立即执行
        MOTOR_FIXED_CHECKSUM
    };
    MOTOR_SERIAL.write(cmd4, sizeof(cmd4));
    delay(10);
    
    Serial.print(F("[电机] 双电机异步步进: 方向="));
    Serial.print(direction == 0x00 ? F("伸出") : F("收回"));
    Serial.print(F(" 步数="));
    Serial.print(steps);
    Serial.print(F(" 速度="));
    Serial.print(speed);
    Serial.println(F("RPM"));
}

// ============================================================
// 读取电机当前位置（同步阻塞方式）
// ============================================================
long motor_read_position(byte motorId) {
    // 清空接收缓冲区
    while (MOTOR_SERIAL.available()) {
        MOTOR_SERIAL.read();
    }
    
    // 发送读取位置命令
    byte cmd[] = {motorId, 0x36, MOTOR_FIXED_CHECKSUM};
    MOTOR_SERIAL.write(cmd, sizeof(cmd));
    
    // 等待响应（超时500ms）
    unsigned long startTime = millis();
    while (millis() - startTime < 500) {
        // ★ 改为等8字节
        if (MOTOR_SERIAL.available() >= 8) {
            byte response[8];
            int bytesRead = 0;
            
            while (MOTOR_SERIAL.available() && bytesRead < 8) {
                response[bytesRead++] = MOTOR_SERIAL.read();
            }
            
            if (bytesRead >= 8) {
                // ★ 校验第7字节
                if (response[0] == motorId && 
                    response[1] == 0x36 && 
                    response[7] == MOTOR_FIXED_CHECKSUM) {
                    
                    byte sign = response[2];
                    long rawPos = ((long)response[3] << 24) | 
                                  ((long)response[4] << 16) | 
                                  ((long)response[5] << 8) | 
                                  (long)response[6];
                    
                    return (sign == 0x01) ? -rawPos : rawPos;
                }
            }
        }
        delay(1);
    }
    
    return -1;
}

// ============================================================
// 批量读取所有电机位置
// ============================================================
uint8_t motor_read_all_positions(long* positions) {
    // ★ 2号电机数据暂不可用，只读取4号电机，两个槽位都填4号数据
    long pos = motor_read_position(0x04);
    positions[0] = pos;  // 2号电机位置 → 使用4号数据
    positions[1] = pos;  // 4号电机位置 → 4号数据
    return (pos >= 0) ? 2 : 0;
}

// ============================================================
// 处理电机返回数据（非阻塞，在 loop 中调用）
// ============================================================
bool motor_process_response() {
    if (MOTOR_SERIAL.available() < 7) {
        return false;
    }
    
    byte response[7];
    int bytesRead = 0;
    
    unsigned long startTime = millis();
    while (MOTOR_SERIAL.available() && bytesRead < 7) {
        response[bytesRead++] = MOTOR_SERIAL.read();
        if (millis() - startTime > 100) break;
    }
    
    if (bytesRead < 7) return false;
    if (response[1] != 0x36) return false;
    if (response[6] != MOTOR_FIXED_CHECKSUM) return false;
    
    _lastPosition = ((long)response[2] << 24) | 
                   ((long)response[3] << 16) | 
                   ((long)response[4] << 8) | 
                   (long)response[5];
    _newPositionData = true;
    
    return true;
}

// ============================================================
// 获取最近一次读取的电机位置
// ============================================================
long get_last_motor_position() {
    return _lastPosition;
}

// ============================================================
// 是否有新的位置数据
// ============================================================
bool has_new_position_data() {
    if (_newPositionData) {
        _newPositionData = false;
        return true;
    }
    return false;
}

