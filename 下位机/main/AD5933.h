#ifndef AD5933_H
#define AD5933_H

#include <Arduino.h>
#include <Wire.h>

// ============================================================
// 硬件I2C配置（BMduino UNO多路硬件I2C选择）
// ============================================================
// BMduino UNO硬件I2C引脚说明：
// I2C0: SDA0(D20/PD1), SCL0(D21/PD0)  - 默认Wire
// I2C1: SDA1(D18/PC1), SCL1(D19/PC0)  - Wire1（需要使用时取消注释）
// ============================================================

// AD5933使用的I2C接口选择
#define AD5933_I2C_BUS Wire1          // 使用I2C0 (D20/D21)
// #define AD5933_I2C_BUS Wire1      // 使用I2C1 (D18/D19) - 需要时取消此行注释并注释上一行

// AD5933硬件I2C引脚定义（仅用于文档说明，实际由Wire库控制）
#define AD5933_SDA_PIN 20            // SDA0 = D20 (PD1)
#define AD5933_SCL_PIN 21            // SCL0 = D21 (PD0)
// 如使用Wire1，则为：
// #define AD5933_SDA_PIN 18         // SDA1 = D18 (PC1)
// #define AD5933_SCL_PIN 19         // SCL1 = D19 (PC0)

// ============================================================
// AD5933 I2C地址定义
// ============================================================
#define AD5933_ADDR 0x0D             // AD5933的I2C从机地址
#define ADDR_PTR 0xB0                // 地址指针命令字节

// ============================================================
// AD5933 寄存器地址映射
// ============================================================
#define CTRL_REG1 0x80               // 控制寄存器1（高字节）
#define CTRL_REG2 0x81               // 控制寄存器2（低字节）
#define START_FREQ_1 0x82            // 起始频率寄存器（24位，高字节）
#define INC_FREQ_1 0x85              // 频率增量寄存器（24位，高字节）
#define NUM_INC_1 0x88               // 增量点数寄存器（9位，高字节）
#define STATUS_REG 0x8F              // 状态寄存器
#define REAL_DATA_1 0x94             // 实部数据寄存器（16位，高字节）
#define REAL_DATA_2 0x95             // 实部数据寄存器（16位，低字节）
#define IMAG_DATA_1 0x96             // 虚部数据寄存器（16位，高字节）
#define IMAG_DATA_2 0x97             // 虚部数据寄存器（16位，低字节）

// ============================================================
// 控制命令定义
// ============================================================
#define CTRL_INIT_START_FREQ 0x10    // 初始化起始频率
#define CTRL_START_FREQ_SWEEP 0x20   // 开始频率扫描
#define CTRL_INCREMENT_FREQ 0x30     // 增加频率
#define CTRL_STANDBY_MODE 0xB0       // 待机模式
#define CTRL_RESET 0x10              // 复位命令
#define CTRL_CLOCK_INTERNAL 0x00     // 使用内部时钟
#define CTRL_PGA_GAIN_X1 0x01        // PGA增益设置为x1

// ============================================================
// 状态寄存器位定义
// ============================================================
#define STATUS_DATA_VALID 0x02       // 数据有效标志位
#define STATUS_SWEEP_DONE 0x04       // 扫描完成标志位

// ============================================================
// 校准系数宏定义（由AD5933_Calibration.ino校准程序生成）
// 使用470Ω参考电阻进行校准后将系数粘贴到此处
// ============================================================
// 20kHz校准系数
#define CAL_GAIN_20KHZ  0.0000009992           // 增益校准因子（从串口输出复制）
#define CAL_PHASE_20KHZ -73             // 相位校准因子（度）（从串口输出复制）
// 50kHz校准系数
#define CAL_GAIN_50KHZ  0.0000010037           // 增益校准因子（从串口输出复制）
#define  CAL_PHASE_50KHZ -54             // 相位校准因子（度）（从串口输出复制）
// 是否使用宏定义校准（1=使用，0=运行时校准）
#define USE_CAL_MACROS  1

// ============================================================
// 频率定义
// ============================================================
#define FREQ_20KHZ 20000        // 测量频率1: 20kHz
#define FREQ_50KHZ 50000        // 测量频率2: 50kHz

// ============================================================
// 校准数据结构体
// 用于存储单个频率点的校准系数
// ============================================================
struct CalData {
    double gain;    // 增益校准因子
    int phase;      // 相位校准因子（度）
};

// ============================================================
// AD5933类定义（仅保留实际使用的函数）
// ============================================================
class AD5933 {
public:
    // ========== 用户级接口函数 ==========
    
    /**
     * @brief 初始化 AD5933 芯片
     * @return true:成功 false:失败
     */
    static bool initialize();
    
    /**
     * @brief 单频校准（多次采样平均）
     * @param freq 校准频率（Hz）
     * @param refResistor 参考电阻值（Ω）
     * @param samples 采样次数
     * @param calData 校准数据结构体（输出）
     * @return true:成功 false:失败
     */
    static bool calibrateFrequency(uint32_t freq, int refResistor, int samples, CalData &calData);
    
    /**
     * @brief 单频阻抗测量（带滤波，阻塞式）
     * @param freq 测量频率（Hz）
     * @param calData 校准数据结构体（输入）
     * @param samples 采样次数
     * @param impedance 阻抗值（输出，Ω）
     * @param phase 相位值（输出，度）
     * @return true:成功 false:失败
     */
    static bool measureImpedance(uint32_t freq, CalData &calData, int samples, double &impedance, double &phase);
    
    /**
     * @brief 配置AD5933到指定频率（为非阻塞分步测量使用）
     * @param freq 目标频率（Hz）
     * @return true:成功 false:失败
     */
    static bool configureForFrequency(uint32_t freq);
    
    /**
     * @brief 执行单次频率扫描并计算阻抗（为非阻塞分步测量使用）
     * @param freq 测量频率（Hz）
     * @param calData 校准数据
     * @param impedance 输出：阻抗值
     * @param phase 输出：相位值
     * @return true:成功 false:失败
     */
    static bool singleSampleMeasurement(uint32_t freq, CalData &calData, double &impedance, double &phase);
    
    /**
     * @brief 冒泡排序（用于数据滤波）
     * @param arr 待排序数组
     * @param len 数组长度
     */
    static void bubbleSort(double arr[], int len);

private:
    // ========== 私有常量 ==========
    static const unsigned long clockSpeed = 16776000;  // 内部时钟频率16.776MHz
    
    // ========== 底层I2C通信函数 ==========
    
    /**
     * @brief 从指定寄存器读取一个字节
     * @param address 寄存器地址
     * @param value 读取值指针（输出）
     * @return 0:成功 其他:错误码
     */
    static int getByte(byte address, byte* value);
    
    /**
     * @brief 向指定寄存器写入一个字节
     * @param address 寄存器地址
     * @param value 写入值
     * @return true:成功 false:失败
     */
    static bool sendByte(byte address, byte value);
    
    // ========== 基础配置函数 ==========
    
    /**
     * @brief 复位AD5933芯片
     * @return true:成功 false:失败
     */
    static bool reset();
    
    /**
     * @brief 设置时钟源
     * @param internal true:使用内部时钟 false:使用外部时钟
     * @return true:成功 false:失败
     */
    static bool setInternalClock(bool internal);
    
    /**
     * @brief 设置起始频率
     * @param freq 起始频率（Hz）
     * @return true:成功 false:失败
     */
    static bool setStartFrequency(unsigned long freq);
    
    /**
     * @brief 设置频率增量
     * @param freq 频率增量（Hz）
     * @return true:成功 false:失败
     */
    static bool setIncrementFrequency(unsigned long freq);
    
    /**
     * @brief 设置频率增量点数
     * @param num 增量点数（0-511）
     * @return true:成功 false:失败
     */
    static bool setNumberIncrements(unsigned int num);
    
    /**
     * @brief 设置PGA增益
     * @param gain CTRL_PGA_GAIN_X1 或 CTRL_PGA_GAIN_X5
     * @return true:成功 false:失败
     */
    static bool setPGAGain(byte gain);
    
    /**
     * @brief 读取状态寄存器
     * @return 状态寄存器值
     */
    static byte readStatusRegister();
    
    /**
     * @brief 获取复数阻抗数据（实部+虚部）
     * @param real 实部数据指针（输出）
     * @param imag 虚部数据指针（输出）
     * @return true:成功 false:超时或失败
     */
    static bool getComplexData(int* real, int* imag);
    
    /**
     * @brief 设置控制模式
     * @param mode 控制模式命令字节
     * @return true:成功 false:失败
     */
    static bool setControlMode(byte mode);
    
    /**
     * @brief 设置电源模式
     * @param mode 电源模式命令
     * @return true:成功 false:失败
     */
    static bool setPowerMode(byte mode);
    
    // ========== 测量与校准函数 ==========
    
    /**
     * @brief 执行频率扫描
     * @param real 实部数据数组（输出）
     * @param imag 虚部数据数组（输出）
     * @param n 扫描点数
     * @return true:成功 false:失败
     */
    static bool frequencySweep(int real[], int imag[], int n);
    
    /**
     * @brief 基础校准函数（单次扫描）
     * @param gain 增益数组（输出）
     * @param phase 相位数组（输出）
     * @param ref 参考电阻值（Ω）
     * @param n 扫描点数
     * @return true:成功 false:失败
     */
    static bool calibrate(double gain[], int phase[], int ref, int n);
};

#endif