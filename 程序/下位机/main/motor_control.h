#ifndef MOTOR_CONTROL_H
#define MOTOR_CONTROL_H

#include <Arduino.h>

/************************************************
 * 硬件配置定义
 ************************************************/
#define MOTOR_SERIAL       Serial3
#define MOTOR_BAUD_RATE    115200

/************************************************
 * 通信协议固定参数
 ************************************************/
#define MOTOR_FIXED_CHECKSUM  0x6B

/************************************************
 * 限位开关引脚定义（NPN输出：无遮挡高电平，遮挡低电平）
 * D4 → 2号电机的限位开关    
 * D5 → 4号电机的限位开关     4电机-d5
 ************************************************/
#define LIMIT_SWITCH_2_PIN   8
#define LIMIT_SWITCH_4_PIN   9

/************************************************
 * 回零速度参数（单位：RPM）
 ************************************************/
#define HOMING_SPEED        5

/************************************************
 * 外部可调用函数声明
 ************************************************/

void motor_init(void);
void To_arm(void);
void back(void);
void motor_stop_all(void);
void motor_homing(void);

// ========== 电机控制函数 ==========

/**
 * @brief  读取电机当前位置（阻塞）
 * @param  motorId 电机ID (0x02 或 0x04)
 * @return 当前位置脉冲数，失败返回 -1
 */
long motor_read_position(byte motorId);

/**
 * @brief  双电机同步步进
 * @param  direction 方向 (0x00=CW正转, 0x01=CCW反转)
 * @param  steps 步进步数
 * @param  speed 速度 (RPM)
 */
void motor_step_sync(byte direction, unsigned long steps, word speed);

/**
 * @brief  双电机异步步进（不同步启动）
 * @param  direction 方向 (0x00=CW正转, 0x01=CCW反转)
 * @param  steps 步进步数
 * @param  speed 速度 (RPM)
 */
void motor_step_async(byte direction, unsigned long steps, word speed);

/**
 * @brief  电机单步步进（单电机）
 * @param  motorId 电机ID (0x02 或 0x04)
 * @param  direction 方向 (0x00=CW正转, 0x01=CCW反转)
 * @param  steps 步进步数
 * @param  speed 速度 (RPM)
 */
void motor_step(byte motorId, byte direction, unsigned long steps, word speed);

// ========== 位置轮询 & 到位检测 ==========

/**
 * @brief  状态轮询（在loop中重复调用）
 * @details 专用状态查询：只发 0x3A，只解析 4字节固定格式
 *          与位置查询完全隔离，不会互相干扰
 */
void motor_poll_positions();

/**
 * @brief  获取缓存的电机位置
 * @param  motorId 电机ID (0x02 或 0x04)
 * @return 位置值，-1表示无效或未读取
 */
long motor_get_cached_position(byte motorId);

/**
 * @brief  检查缓存位置是否有效
 */
bool motor_is_position_valid(byte motorId);

/**
 * @brief  批量读取所有电机位置
 * @param  positions 存储位置的数组（至少2个元素）
 * @return 成功读取的电机数量
 */
uint8_t motor_read_all_positions(long* positions);

/**
 * @brief  检查两个电机是否都到达目标位置
 * @return true:两个电机都到达目标位置
 */
bool motor_both_reached();

/**
 * @brief  获取缓存的到位状态
 */
bool motor_get_cached_reached(byte motorId);

/**
 * @brief  检查到位状态是否有效
 * @return true:到位状态有效 false:需要重新查询
 */
bool motor_is_reached_valid();

/**
 * @brief  获取运动开始时间
 * @return 运动开始时的millis()值，0表示电机未在运动
 */
unsigned long motor_get_move_start_time();

// ★ 辅助函数
void motor_notify_move_started();   // 通知电机开始运动
void motor_clear_reached_flag();    // ★ 清除到位标志（运动开始时调用）
void motor_invalidate_reached();    // 标记到位状态为待验证

// ========== 兼容接口 ==========

/**
 * @brief  获取最近一次读取的电机位置
 */
long get_last_motor_position();

/**
 * @brief  是否有新的位置数据
 */
bool has_new_position_data();

#endif