#ifndef DISPLAY_HELPER_H
#define DISPLAY_HELPER_H

#include <Adafruit_SSD1306.h>

// ============================================================
// OLED硬件I2C配置（BMduino UNO多路硬件I2C选择）
// ============================================================
// OLED使用的I2C接口选择
#define OLED_I2C_BUS Wire1             // 使用I2C0 (D20/D21)
// #define OLED_I2C_BUS Wire1         // 使用I2C1 (D18/D19) - 需要时取消此行注释并注释上一行

// OLED硬件I2C引脚定义（仅用于文档说明，实际由Wire库控制）
#define OLED_SDA_PIN 20               // SDA0 = D20 (PD1)
#define OLED_SCL_PIN 21               // SCL0 = D21 (PD0)
// 如使用Wire1，则为：
// #define OLED_SDA_PIN 18            // SDA1 = D18 (PC1)
// #define OLED_SCL_PIN 19            // SCL1 = D19 (PC0)

// ============================================================
// 系统参数配置
// ============================================================
#define REF_RESIST 470          // 校准参考电阻值（欧姆）
//#define CAL_SAMPLES 10          // 校准时的采样次数（取平均）
//#define MEAS_SAMPLES 10         // 测量时的采样次数（滤波用）
#define CAL_SAMPLES 10   // 临时改为 1 次（原来是10次）
#define MEAS_SAMPLES 10  // 临时改为 1 次
#define FREQ_20KHZ 20000        // 测量频率1: 20kHz
#define FREQ_50KHZ 50000        // 测量频率2: 50kHz

// ============================================================
// 硬件配置
// ============================================================
#define SCREEN_WIDTH 128        // OLED显示屏宽度（像素）
#define SCREEN_HEIGHT 64        // OLED显示屏高度（像素）
#define OLED_ADDR 0x3C          // OLED的I2C地址

// ============================================================
// OLED显示辅助类
// 封装所有OLED显示相关函数
// ============================================================
class DisplayHelper {
public:
    /**
     * @brief 初始化显示辅助类
     * @param disp OLED显示对象指针
     */
    static void init(Adafruit_SSD1306* disp);
    
    // ========== 校准界面显示函数 ==========
    
    /**
     * @brief 显示校准开始界面
     * @param refResistor 参考电阻值（Ω）
     */
    static void showCalibrationStart(int refResistor);
    
    /**
     * @brief 更新校准进度（显示当前校准的频率）
     * @param freqText 频率文本（如 "20kHz..."）
     */
    static void updateCalibrationProgress(const char* freqText);
    
    /**
     * @brief 显示校准失败界面
     * @param freqText 失败的频率文本（如 "20kHz"）
     */
    static void showCalibrationFailed(const char* freqText);
    
    /**
     * @brief 显示校准成功界面
     */
    static void showCalibrationSuccess();
    
    // ========== 测量结果显示函数 ==========
    
    /**
     * @brief 显示双频测量结果
     * @param z20k 20kHz阻抗值（Ω）
     * @param z50k 50kHz阻抗值（Ω）
     * @param ratio 阻抗比值
     */
    static void showMeasurementResult(double z20k, double z50k, double ratio);
    
    /**
     * @brief 显示测量错误界面
     */
    static void showMeasurementError();
    
private:
    static Adafruit_SSD1306* display;  // OLED显示对象指针
    
    /**
     * @brief 绘制标题栏（包含标题和分隔线）
     * @param title 标题文本
     */
    static void drawTitleBar(const char* title);
};

#endif