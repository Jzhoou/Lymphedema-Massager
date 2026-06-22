#line 1 "D:\\table\\第四阶段\\系统\\临时dowmcomputer\\临时430\\main\\MY2901.cpp"
/*
 * ================================================================
 *  MY2901.cpp — MY2901 驱动实现
 * ================================================================
 */

#include "MY2901.h"

// -------------------- 构造函数 --------------------
MY2901::MY2901(Stream *serial)
  : _serial(serial),       // 保存串口对象
    _rxIdx(0),             // 接收索引归零
    _lastRxTime(0),        // 上次接收时间归零
    _format(MY2901_BCD),   // 默认BCD格式
    _newData(false)        // 还没有新数据
{
  // 所有AD值初始化为0
  memset(_adValues, 0, sizeof(_adValues));
}

// -------------------- 初始化 --------------------
void MY2901::begin() {
  _rxIdx = 0;
  _newData = false;
  memset(_adValues, 0, sizeof(_adValues));
  // 使用出厂默认设置，不发送配置命令
  // （SoftwareSerial在115200波特率下发送命令不可靠）
}

// -------------------- 更新（在loop中反复调用） --------------------
bool MY2901::update() {
  _newData = false;

  // 读取串口缓冲区里所有可用的字节
  while (_serial->available()) {
    uint8_t b = _serial->read();         // 读一个字节
    unsigned long now = millis();         // 当前时间

    // === 帧超时检测 ===
    // 如果已经开始接收(rxIdx>0)，但超过200ms没收到下一个字节
    // 说明数据中断了，需要重新开始寻找帧头
    if (_rxIdx > 0 && (now - _lastRxTime) > MY2901_FRAME_TIMEOUT) {
      _rxIdx = 0;
    }
    _lastRxTime = now;  // 记录这次收到字节的时间

    // === 第1步：寻找帧头 0xFF ===
    if (_rxIdx == 0) {
      if (b == MY2901_FRAME_HEADER) {
        _rxBuf[0] = b;   // 存储帧头
        _rxIdx = 1;       // 开始接收后续字节
      }
      continue;           // 还没找到帧头就继续找
    }

    // === 第2步：存储后续字节 ===
    if (_rxIdx < MY2901_FRAME_LEN) {
      _rxBuf[_rxIdx] = b;
      _rxIdx++;
    } else {
      // 超出帧长度，出错了，重新开始
      _rxIdx = 0;
      continue;
    }

    // === 第3步：收满20字节 = 一帧完整数据 ===
    if (_rxIdx >= MY2901_FRAME_LEN) {
      parseFrame(_rxBuf);   // 解析这一帧
      _rxIdx = 0;           // 重置，准备接收下一帧
    }
  }

  return _newData;
}

// -------------------- 获取单通道AD值 --------------------
uint16_t MY2901::getAD(uint8_t channel) {
  if (channel >= MY2901_TOTAL_CHANNELS) return 0;
  return _adValues[channel];
}

// -------------------- 批量获取AD值 --------------------
void MY2901::getMultiAD(uint16_t *destArray, uint8_t count) {
  // count不能超过总通道数
  if (count > MY2901_TOTAL_CHANNELS) {
    count = MY2901_TOTAL_CHANNELS;
  }
  // 把内部存储的AD值复制到用户提供的数组
  for (uint8_t i = 0; i < count; i++) {
    destArray[i] = _adValues[i];
  }
}

// -------------------- 解析数据帧 --------------------
void MY2901::parseFrame(uint8_t *frame) {
  // 八通道帧结构:
  // frame[0]  = 0xFF (帧头)
  // frame[1]  = 命令1
  // frame[2]  = 命令2
  // frame[3]  = S0高字节    frame[4]  = S0低字节
  // frame[5]  = S1高字节    frame[6]  = S1低字节
  // frame[7]  = S2高字节    frame[8]  = S2低字节
  // frame[9]  = S3高字节    frame[10] = S3低字节
  // frame[11] = S4高字节    frame[12] = S4低字节
  // frame[13] = S5高字节    frame[14] = S5低字节
  // frame[15] = S6高字节    frame[16] = S6低字节
  // frame[17] = S7高字节    frame[18] = S7低字节
  // frame[19] = 校验和

  for (uint8_t ch = 0; ch < MY2901_TOTAL_CHANNELS; ch++) {
    uint8_t hi = frame[3 + ch * 2];   // 高字节
    uint8_t lo = frame[4 + ch * 2];   // 低字节

    if (_format == MY2901_HEX) {
      _adValues[ch] = decodeHEX(hi, lo);
    } else {
      _adValues[ch] = decodeBCD(hi, lo);
    }
  }

  _newData = true;  // 标记有新数据
}

// -------------------- BCD解码 --------------------
// 例: hi=0x12, lo=0x34 → 1234
uint16_t MY2901::decodeBCD(uint8_t hi, uint8_t lo) {
  uint16_t thousands = (hi >> 4) & 0x0F;   // 千位
  uint16_t hundreds  = hi & 0x0F;           // 百位
  uint16_t tens      = (lo >> 4) & 0x0F;   // 十位
  uint16_t ones      = lo & 0x0F;           // 个位
  return thousands * 1000 + hundreds * 100 + tens * 10 + ones;
}

// -------------------- HEX解码 --------------------
// 例: hi=0x0C, lo=0xD3 → 0x0CD3 = 3283
uint16_t MY2901::decodeHEX(uint8_t hi, uint8_t lo) {
  return ((uint16_t)hi << 8) | lo;
}