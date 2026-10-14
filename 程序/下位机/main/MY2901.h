/*
 * ================================================================
 *  MY2901.h — MY2901 八通道数字转换模块 驱动程序
 * ================================================================
 *
 *  功能说明:
 *    MY2901 是一个把薄膜压力传感器的模拟信号转成数字值(AD值)的模块。
 *    它通过串口(UART)以固定格式发送数据给Arduino。
 *
 *  数据帧格式 (八通道, 20字节):
 *    字节1:    0xFF (帧头，表示一帧数据的开始)
 *    字节2~3:  命令字节 (通常是 0x00 0x00)
 *    字节4~5:  通道S0的AD值 (高字节在前)
 *    字节6~7:  通道S1的AD值
 *    字节8~9:  通道S2的AD值
 *    字节10~11: 通道S3的AD值
 *    字节12~13: 通道S4的AD值
 *    字节14~15: 通道S5的AD值
 *    字节16~17: 通道S6的AD值
 *    字节18~19: 通道S7的AD值
 *    字节20:   校验和
 *
 *  通讯参数:
 *    波特率: 115200
 *    数据位: 8
 *    停止位: 1
 *    校验位: 无
 *
 * ================================================================
 */

#ifndef MY2901_H
#define MY2901_H

#include <Arduino.h>

// ==================== MY2901 硬件参数 ====================

#define MY2901_TOTAL_CHANNELS  8      // MY2901总共有8个通道
#define MY2901_FRAME_LEN       20     // 八通道数据帧长度 (字节)
#define MY2901_CMD_FRAME_LEN   12     // 命令帧长度 (字节)
#define MY2901_FRAME_HEADER    0xFF   // 帧头标记
#define MY2901_BAUD_RATE       115200 // 串口波特率
#define MY2901_FRAME_TIMEOUT   200    // 帧超时时间(毫秒)，超过这个时间没收到下一个字节就重新开始

// ==================== 数据格式枚举 ====================
// MY2901可以用两种格式发送AD值，默认是BCD
enum MY2901_Format {
  MY2901_BCD = 0,    // BCD码: 0x34 0x21 表示 3421
  MY2901_HEX = 1     // 十六进制: 0x0C 0xD3 表示 0x0CD3 = 3283
};


// ==================== MY2901 类 ====================
class MY2901 {

public:

  /*
   * 构造函数
   * 参数 serial: 串口对象的指针
   *   - 如果用 SoftwareSerial，就传 &mySerial
   *   - 如果用 Arduino Mega 的 Serial1，就传 &Serial1
   */
  MY2901(Stream *serial);

  /*
   * 初始化
   * 在 setup() 里调用一次
   */
  void begin();

  /*
   * 更新数据
   * 在 loop() 里反复调用，它会自动接收和解析MY2901发来的数据
   * 返回值:
   *   true  = 刚刚收到了一帧完整数据
   *   false = 还没收到新数据
   */
  bool update();

  /*
   * 获取某个通道的AD值
   * 参数 channel: 通道号 0~7
   * 返回值: AD值 0~4095
   */
  uint16_t getAD(uint8_t channel);

  /*
   * 批量获取多个通道的AD值，存到你提供的数组里
   * 参数 destArray: 你的数组（用来存结果）
   * 参数 count:     要获取几个通道（从通道0开始）
   */
  void getMultiAD(uint16_t *destArray, uint8_t count);


private:

  Stream        *_serial;                         // 串口对象
  uint8_t        _rxBuf[MY2901_FRAME_LEN + 2];   // 接收缓冲区
  uint8_t        _rxIdx;                          // 当前接收到第几个字节
  unsigned long  _lastRxTime;                     // 上次收到字节的时间
  uint16_t       _adValues[MY2901_TOTAL_CHANNELS];// 8个通道的AD值
  MY2901_Format  _format;                         // 当前数据格式
  bool           _newData;                        // 是否有新数据

  // 内部函数（你不需要直接调用）
  void     parseFrame(uint8_t *frame);            // 解析一帧数据
  uint16_t decodeBCD(uint8_t hi, uint8_t lo);     // BCD解码
  uint16_t decodeHEX(uint8_t hi, uint8_t lo);     // HEX解码
};

#endif // MY2901_H