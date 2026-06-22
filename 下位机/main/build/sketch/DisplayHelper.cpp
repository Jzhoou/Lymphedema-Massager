#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\DisplayHelper.cpp"
#include "DisplayHelper.h"

// 初始化静态成员变量
Adafruit_SSD1306* DisplayHelper::display = nullptr;

/**
 * @brief 初始化显示辅助类
 * @details 保存OLED对象指针供后续使用
 */
void DisplayHelper::init(Adafruit_SSD1306* disp) {
    display = disp;
}

/**
 * @brief 绘制标题栏
 * @details 显示标题文本并在下方绘制分隔线
 */
void DisplayHelper::drawTitleBar(const char* title) {
    display->setTextSize(1);
    display->setCursor(0, 0);
    display->println(title);
    display->drawLine(0, 9, 127, 9, 1);  // 绘制横线分隔
}

// ============================================================
// 校准界面显示函数
// ============================================================

/**
 * @brief 显示校准开始界面
 * @details 显示校准标题、参考电阻值和校准提示
 */
void DisplayHelper::showCalibrationStart(int refResistor) {
    display->clearDisplay();
    display->setTextColor(1);
    
    // 标题
    drawTitleBar("Dual Freq Cal");
    
    // 显示参考电阻值
    display->setCursor(0, 18);
    display->print(F("Ref R: "));
    display->print(refResistor);
    display->println(F(" Ohm"));
    
    // 校准提示
    display->setCursor(0, 35);
    display->println(F("Calibrating..."));
    
    display->display();
}

/**
 * @brief 更新校准进度
 * @details 在屏幕底部显示当前正在校准的频率
 */
void DisplayHelper::updateCalibrationProgress(const char* freqText) {
    // 清除底部区域（不清除整个屏幕）
    display->fillRect(0, 50, 128, 14, 0);  // 黑色矩形覆盖
    
    // 显示当前频率
    display->setCursor(0, 50);
    display->print(freqText);
    
    display->display();
}

/**
 * @brief 显示校准失败界面
 * @details 清屏并显示失败信息
 */
void DisplayHelper::showCalibrationFailed(const char* freqText) {
    display->clearDisplay();
    display->setTextSize(1);
    display->setCursor(0, 20);
    
    // 显示失败的频率
    display->print(freqText);
    display->println(F(" Cal FAIL!"));
    
    // 提示检查硬件
    display->setCursor(0, 35);
    display->println(F("Check HW & Ref R"));
    
    display->display();
}

/**
 * @brief 显示校准成功界面
 * @details 显示成功提示和即将开始测量的信息
 */
void DisplayHelper::showCalibrationSuccess() {
    display->clearDisplay();
    display->setTextSize(1);
    
    // 标题
    drawTitleBar("Cal Success!");
    
    // 成功信息
    display->setCursor(0, 18);
    display->println(F("20k+50k Ready"));
    
    // 提示即将开始测量
    display->setCursor(0, 50);
    display->println(F("Start Measure..."));
    
    display->display();
}

// ============================================================
// 测量结果显示函数
// ============================================================

/**
 * @brief 显示双频测量结果
 * @details 显示20kHz和50kHz的阻抗值，以及阻抗比值（重点放大显示）
 */
void DisplayHelper::showMeasurementResult(double z20k, double z50k, double ratio) {
    display->clearDisplay();
    
    // 标题
    display->setTextSize(1);
    drawTitleBar("Dual Freq Z Meter");
    
    // 显示两个频点的阻抗（左右排列）
    display->setCursor(0, 18);
    display->print(F("20k:"));
    display->print(z20k, 1);  // 保留1位小数
    
    display->setCursor(64, 18);
    display->print(F("50k:"));
    display->print(z50k, 1);  // 保留1位小数
    
    // 放大显示阻抗比值（重点数据）
    display->setTextSize(2);
    display->setCursor(0, 35);
    display->print(F("R:"));
    display->print(ratio, 3);  // 保留3位小数
    
    display->display();
}

/**
 * @brief 显示测量错误界面
 * @details 清屏并显示错误提示
 */
void DisplayHelper::showMeasurementError() {
    display->clearDisplay();
    display->setTextSize(1);
    display->setCursor(0, 20);
    
    display->println(F("Measure Error!"));
    
    // 提示检查硬件
    display->setCursor(0, 35);
    display->println(F("Recheck HW"));
    
    display->display();
}