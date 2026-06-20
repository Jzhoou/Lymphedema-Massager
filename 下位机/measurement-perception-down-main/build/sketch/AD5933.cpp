#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\AD5933.cpp"
#include "AD5933.h"
#include <math.h>

// ============================================================
// 底层I2C通信函数
// ============================================================

/**
 * @brief 从AD5933读取单个字节
 * @details 先设置地址指针，再请求数据
 */
int AD5933::getByte(byte address, byte* value) {
    // 开始I2C传输，设置寄存器指针（使用宏定义的I2C总线）
    AD5933_I2C_BUS.beginTransmission(AD5933_ADDR);
    AD5933_I2C_BUS.write(ADDR_PTR);           // 发送地址指针命令
    AD5933_I2C_BUS.write(address);            // 发送目标寄存器地址
    if (AD5933_I2C_BUS.endTransmission() != 0) return 4;  // 传输失败
    
    // 请求读取1字节数据
    AD5933_I2C_BUS.requestFrom((uint8_t)AD5933_ADDR, (uint8_t)1);
    if (AD5933_I2C_BUS.available()) {
        *value = AD5933_I2C_BUS.read();       // 读取数据
        return 0;                             // 成功
    }
    return 4;                                 // 无数据可读
}

/**
 * @brief 向AD5933写入单个字节
 * @details 直接向指定寄存器写入数据
 */
bool AD5933::sendByte(byte address, byte value) {
    AD5933_I2C_BUS.beginTransmission(AD5933_ADDR);
    AD5933_I2C_BUS.write(address);            // 发送寄存器地址
    AD5933_I2C_BUS.write(value);              // 发送数据
    return AD5933_I2C_BUS.endTransmission() == 0;  // 返回是否成功
}

// ============================================================
// 基础配置函数
// ============================================================

/**
 * @brief 复位AD5933
 * @details 通过设置控制寄存器2的复位位来复位芯片
 */
bool AD5933::reset() {
    byte val;
    if (getByte(CTRL_REG2, &val) != 0) return false;  // 读取当前值
    val |= CTRL_RESET;                                 // 设置复位位
    return sendByte(CTRL_REG2, val);                   // 写回寄存器
}

/**
 * @brief 设置控制模式
 * @details 修改控制寄存器1的高4位，保留低4位设置
 */
bool AD5933::setControlMode(byte mode) {
    byte val;
    if (getByte(CTRL_REG1, &val) != 0) return false;
    val = (val & 0x0F) | mode;      // 保留低4位，更新高4位
    return sendByte(CTRL_REG1, val);
}

/**
 * @brief 设置时钟源
 * @details 修改控制寄存器2的时钟选择位（bit3）
 */
bool AD5933::setInternalClock(bool internal) {
    byte val;
    if (getByte(CTRL_REG2, &val) != 0) return false;
    if (internal)
        val &= ~0x08;               // 清除bit3 = 内部时钟
    else
        val |= 0x08;                // 设置bit3 = 外部时钟
    return sendByte(CTRL_REG2, val);
}

/**
 * @brief 设置起始频率
 * @details 频率码计算公式：freqCode = freq / (MCLK/4) * 2^27
 *          MCLK = 16.776MHz（内部时钟）
 *          写入3个寄存器（24位）
 */
bool AD5933::setStartFrequency(unsigned long freq) {
    // 计算24位频率码
    long freqCode = (long)((double)freq / ((double)clockSpeed / 4.0) * pow(2, 27));
    
    // 分解为3个字节并写入寄存器
    return sendByte(START_FREQ_1, (freqCode >> 16) & 0xFF) &&      // 高字节
           sendByte(START_FREQ_1 + 1, (freqCode >> 8) & 0xFF) &&   // 中字节
           sendByte(START_FREQ_1 + 2, freqCode & 0xFF);            // 低字节
}

/**
 * @brief 设置频率增量
 * @details 计算方法与起始频率相同
 */
bool AD5933::setIncrementFrequency(unsigned long freq) {
    long freqCode = (long)((double)freq / ((double)clockSpeed / 4.0) * pow(2, 27));
    
    return sendByte(INC_FREQ_1, (freqCode >> 16) & 0xFF) &&
           sendByte(INC_FREQ_1 + 1, (freqCode >> 8) & 0xFF) &&
           sendByte(INC_FREQ_1 + 2, freqCode & 0xFF);
}

/**
 * @brief 设置频率增量点数
 * @details 9位数据，范围0-511，写入2个寄存器
 */
bool AD5933::setNumberIncrements(unsigned int num) {
    return sendByte(NUM_INC_1, (num >> 8) & 0x01) &&    // 高位（仅1位有效）
           sendByte(NUM_INC_1 + 1, num & 0xFF);         // 低8位
}

/**
 * @brief 设置PGA增益
 * @details 修改控制寄存器1的bit0
 */
bool AD5933::setPGAGain(byte gain) {
    byte val;
    if (getByte(CTRL_REG1, &val) != 0) return false;
    if (gain == CTRL_PGA_GAIN_X1)
        val |= CTRL_PGA_GAIN_X1;    // 设置bit0 = x1增益
    else
        val &= ~CTRL_PGA_GAIN_X1;   // 清除bit0 = x5增益
    return sendByte(CTRL_REG1, val);
}

/**
 * @brief 设置电源模式
 * @details 通过控制模式设置电源状态
 */
bool AD5933::setPowerMode(byte mode) {
    return setControlMode(mode);
}

/**
 * @brief 读取状态寄存器
 * @return 状态值，0xFF表示读取失败
 */
byte AD5933::readStatusRegister() {
    byte val;
    return (getByte(STATUS_REG, &val) == 0) ? val : 0xFF;
}

// ============================================================
// 数据采集函数
// ============================================================

/**
 * @brief 获取复数阻抗数据
 * @details 等待数据有效标志，然后读取实部和虚部
 *          数据为16位有符号整数
 */
bool AD5933::getComplexData(int* real, int* imag) {
    // 等待数据有效（最长1秒超时）
    int timeout = 1000;
    while ((readStatusRegister() & STATUS_DATA_VALID) != STATUS_DATA_VALID) {
        if (--timeout == 0) return false;  // 超时
        delay(1);
    }
    
    // 读取实部（2字节）
    byte r1, r2;
    if (getByte(REAL_DATA_1, &r1) != 0) return false;
    if (getByte(REAL_DATA_2, &r2) != 0) return false;
    *real = (int16_t)((r1 << 8) | r2);  // 组合为16位有符号数
    
    // 读取虚部（2字节）
    byte i1, i2;
    if (getByte(IMAG_DATA_1, &i1) != 0) return false;
    if (getByte(IMAG_DATA_2, &i2) != 0) return false;
    *imag = (int16_t)((i1 << 8) | i2);  // 组合为16位有符号数
    
    return true;
}

/**
 * @brief 执行频率扫描
 * @details 完整的扫描流程：
 *          1. 进入待机模式
 *          2. 初始化起始频率
 *          3. 开始扫描
 *          4. 逐点读取数据并增加频率
 *          5. 返回待机模式
 */
bool AD5933::frequencySweep(int real[], int imag[], int n) {
  if (!setPowerMode(CTRL_STANDBY_MODE)) return false;
  if (!setControlMode(CTRL_INIT_START_FREQ)) return false;
  delay(50);  // 100→50ms 关键优化
  if (!setControlMode(CTRL_START_FREQ_SWEEP)) return false;
  
  int i = 0;
  while (i < n && (readStatusRegister() & STATUS_SWEEP_DONE) != STATUS_SWEEP_DONE) {
    if (!getComplexData(&real[i], &imag[i])) return false;
    i++;
    if (i < n) setControlMode(CTRL_INCREMENT_FREQ);
  }
  
  setPowerMode(CTRL_STANDBY_MODE);
  return true;
}
/**
 * @brief 基础校准函数
 * @details 通过已知参考电阻计算增益和相位校准因子
 *          增益因子 = 1 / (|Z| * Rref)
 *          相位因子 = arctan(imag/real)
 */
bool AD5933::calibrate(double gain[], int phase[], int ref, int n) {
    int real[n], imag[n];
    
    // 执行频率扫描获取原始数据
    if (!frequencySweep(real, imag, n)) return false;
    
    // 计算每个频点的校准因子
    for (int i = 0; i < n; i++) {
        // 计算幅值 |Z| = sqrt(real^2 + imag^2)
        double magnitude = sqrt(pow(real[i], 2) + pow(imag[i], 2));
        
        // 增益因子
        gain[i] = 1.0 / ((double)ref * magnitude);
        
        // 相位因子（转换为角度）
        phase[i] = (int)(atan2(imag[i], real[i]) * 180.0 / M_PI);
    }
    return true;
}

// ============================================================
// 高级功能函数（用户级接口）
// ============================================================

/**
 * @brief 冒泡排序算法
 * @details 用于测量数据滤波，去除极值点
 */
void AD5933::bubbleSort(double arr[], int len) {
    for (int i = 0; i < len - 1; i++) {
        for (int j = 0; j < len - i - 1; j++) {
            if (arr[j] > arr[j + 1]) {
                // 交换元素
                double temp = arr[j];
                arr[j] = arr[j + 1];
                arr[j + 1] = temp;
            }
        }
    }
}

/**
 * @brief 单频校准函数（多次采样平均）
 * @details 执行流程：
 *          1. 配置AD5933到指定频率
 *          2. 执行首次校准
 *          3. 多次重复校准并累加
 *          4. 计算平均校准因子
 */
bool AD5933::calibrateFrequency(uint32_t freq, int refResistor, int samples, CalData &calData) {
  // 移除所有 Serial.print
  
  reset();
  setInternalClock(true);
  setStartFrequency(freq);
  setIncrementFrequency(1);
  setNumberIncrements(0);
  setPGAGain(CTRL_PGA_GAIN_X1);
  
  double gain[1];
  int phase[1];
  
  if (!calibrate(gain, phase, refResistor, 1)) {
    return false;
  }
  
  double gainSum = gain[0];
  int phaseSum = phase[0];
  int validCount = 1;
  
  for (int n = 0; n < samples - 1; n++) {
    delay(50);  // 200→50ms
    if (calibrate(gain, phase, refResistor, 1)) {
      gainSum += gain[0];
      phaseSum += phase[0];
      validCount++;
    }
  }
  
  calData.gain = gainSum / validCount;
  calData.phase = phaseSum / validCount;
  
  return true;
}
/**
 * @brief 单频阻抗测量（带滤波）
 * @details 执行流程：
 *          1. 配置AD5933到指定频率
 *          2. 多次采样获取原始数据
 *          3. 使用校准因子计算阻抗和相位
 *          4. 排序去除极值后取平均
 */
bool AD5933::measureImpedance(uint32_t freq, CalData &calData, int samples, double &impedance, double &phase) {
  reset();
  setInternalClock(true);
  setStartFrequency(freq);
  setIncrementFrequency(1);
  setNumberIncrements(0);
  setPGAGain(CTRL_PGA_GAIN_X1);
  
  double zArray[samples];
  double phaseArray[samples];
  int real[1], imag[1];
  int valid = 0;
  
  for (int i = 0; i < samples; i++) {
    if (frequencySweep(real, imag, 1)) {
      double mag = sqrt(real[0] * real[0] + imag[0] * imag[0]);
      double imp = 1.0 / (mag * calData.gain);
      double ph = atan2(imag[0], real[0]) * 180.0 / M_PI - calData.phase;
      
      zArray[valid] = imp;
      phaseArray[valid] = ph;
      valid++;
    }
    delay(10);  // 20→10ms
  }
  
  if (valid < 3) return false;
  
  bubbleSort(zArray, valid);
  bubbleSort(phaseArray, valid);
  
  double sumZ = 0, sumPh = 0;
  int count = valid - 2;
  for (int i = 1; i < valid - 1; i++) {
    sumZ += zArray[i];
    sumPh += phaseArray[i];
  }
  
  impedance = sumZ / count;
  phase = sumPh / count;
  
  return true;
}
/**
 * @brief 初始化 AD5933 芯片
 * @details 复位芯片、设置时钟源和增益
 */
bool AD5933::initialize() {
#if AD5933_DEBUG
    Serial.println(F("========== AD5933 初始化 =========="));
#endif
    
    // 1. 测试 I2C 通信
    AD5933_I2C_BUS.beginTransmission(AD5933_ADDR);
    byte error = AD5933_I2C_BUS.endTransmission();
    if (error != 0) {
#if AD5933_DEBUG
        Serial.print(F("[ERROR] AD5933 未响应 (I2C错误码: "));
        Serial.print(error);
        Serial.println(F(")"));
        Serial.println(F("请检查:"));
        Serial.println(F("  1. SDA1(D18) 和 SCL1(D19) 接线"));
        Serial.println(F("  2. AD5933 供电 (3.3V)"));
        Serial.println(F("  3. I2C 上拉电阻 (2.2kΩ-10kΩ)"));
#endif
        return false;
    }
#if AD5933_DEBUG
    Serial.println(F("[OK] AD5933 I2C 通信正常"));
#endif
    
    // 2. 复位芯片
#if AD5933_DEBUG
    Serial.println(F("[DEBUG] 复位芯片..."));
#endif
    if (!reset()) {
#if AD5933_DEBUG
        Serial.println(F("[ERROR] 复位失败"));
#endif
        return false;
    }
    delay(100);
    
    // 3. 设置内部时钟
#if AD5933_DEBUG
    Serial.println(F("[DEBUG] 配置内部时钟..."));
#endif
    if (!setInternalClock(true)) {
#if AD5933_DEBUG
        Serial.println(F("[ERROR] 时钟配置失败"));
#endif
        return false;
    }
    delay(50);
    
    // 4. 设置 PGA 增益
#if AD5933_DEBUG
    Serial.println(F("[DEBUG] 设置增益 x1..."));
#endif
    if (!setPGAGain(CTRL_PGA_GAIN_X1)) {
#if AD5933_DEBUG
        Serial.println(F("[ERROR] 增益配置失败"));
#endif
        return false;
    }
    delay(50);
    
    // 5. 验证状态寄存器
    byte statusVal = readStatusRegister();
    if (statusVal == 0xFF) {
#if AD5933_DEBUG
        Serial.println(F("[ERROR] 无法读取状态寄存器"));
#endif
        return false;
    }
#if AD5933_DEBUG
    Serial.print(F("[DEBUG] 状态寄存器: 0x"));
    Serial.println(statusVal, HEX);
    Serial.println(F("[OK] AD5933 初始化完成"));
#endif
    
    return true;
}