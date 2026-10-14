using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Upcomputer.Core.Services
{
    /// <summary>
    /// 水肿预测模型参数
    /// <para>从 JSON 文件加载的逻辑回归模型参数，包括特征名称列表、标准化参数和回归系数。</para>
    /// </summary>
    public class EdemaModelParams
    {
        /// <summary>特征名称列表（如 "age", "bmi", "sex", "rRatio"）</summary>
        public List<string> features { get; set; } = new();

        /// <summary>标准化均值（StandardScaler 的 mean），与 features 一一对应</summary>
        public List<double> scaler_mean { get; set; } = new();

        /// <summary>标准化缩放因子（StandardScaler 的 scale），与 features 一一对应</summary>
        public List<double> scaler_scale { get; set; } = new();

        /// <summary>逻辑回归系数向量，与 features 一一对应</summary>
        public List<double> coef { get; set; } = new();

        /// <summary>逻辑回归截距项</summary>
        public double intercept { get; set; }
    }

    /// <summary>
    /// 水肿预测结果
    /// <para>包含预测概率、是否阳性、风险等级及对应的建议描述。</para>
    /// </summary>
    public class EdemaPredictionResult
    {
        /// <summary>阳性概率（0.0 ~ 1.0）</summary>
        public double Probability { get; set; }

        /// <summary>是否判定为阳性（概率 >= 0.5）</summary>
        public bool IsPositive { get; set; }

        /// <summary>风险等级标签（如 "轻度水肿风险"）</summary>
        public string Severity { get; set; } = string.Empty;

        /// <summary>风险等级详细描述（如 "风险较低，建议定期观察"）</summary>
        public string SeverityDescription { get; set; } = string.Empty;
    }

    /// <summary>
    /// 水肿预测器
    /// <para>
    /// 基于逻辑回归模型进行水肿风险评估。模型参数从 JSON 文件加载，
    /// 预测流程：原始特征 → StandardScaler 标准化 → 线性组合 z → Sigmoid(z) → 概率。
    /// </para>
    /// </summary>
    public class EdemaPredictor
    {
        private readonly EdemaModelParams _params;

        /// <summary>
        /// 创建水肿预测器实例
        /// </summary>
        /// <param name="modelParamsPath">模型参数 JSON 文件路径</param>
        /// <exception cref="ArgumentException">路径为空时抛出</exception>
        /// <exception cref="FileNotFoundException">文件不存在时抛出</exception>
        /// <exception cref="InvalidDataException">JSON 无法解析时抛出</exception>
        public EdemaPredictor(string modelParamsPath)
        {
            if (string.IsNullOrWhiteSpace(modelParamsPath))
                throw new ArgumentException("modelParamsPath is required", nameof(modelParamsPath));

            if (!File.Exists(modelParamsPath))
                throw new FileNotFoundException("模型参数文件不存在", modelParamsPath);

            var json = File.ReadAllText(modelParamsPath);
            _params = JsonConvert.DeserializeObject<EdemaModelParams>(json) ?? throw new InvalidDataException("无法解析模型参数文件");
        }

        /// <summary>
        /// Sigmoid 激活函数
        /// <para>针对正/负输入采用数值稳定的计算方式，避免 exp 溢出。</para>
        /// </summary>
        /// <param name="z">线性组合值</param>
        /// <returns>0 到 1 之间的概率值</returns>
        private double Sigmoid(double z)
        {
            // 数值稳定性优化：z>=0 和 z<0 使用不同的等价公式，避免 Math.Exp 溢出
            if (z >= 0) return 1.0 / (1.0 + Math.Exp(-z));
            var expZ = Math.Exp(z);
            return expZ / (1.0 + expZ);
        }

        /// <summary>
        /// 执行水肿风险预测
        /// </summary>
        /// <param name="age">患者年龄</param>
        /// <param name="sex">性别（0=女, 1=男）</param>
        /// <param name="bmi">体质指数</param>
        /// <param name="rRatio">阻抗比值（R-ratio）</param>
        /// <returns>预测结果，包含概率、风险等级和建议</returns>
        public EdemaPredictionResult Predict(double age, int sex, double bmi, double rRatio)
        {
            // 特征排列顺序须与模型训练时一致：age, bmi, sex, rRatio
            var features = new double[] { age, bmi, sex, rRatio };

            // StandardScaler 标准化：(x - mean) / scale
            var scaled = new double[features.Length];
            for (int i = 0; i < features.Length && i < _params.scaler_mean.Count && i < _params.scaler_scale.Count; i++)
            {
                scaled[i] = (features[i] - _params.scaler_mean[i]) / _params.scaler_scale[i];
            }

            // 线性组合 z = intercept + Σ(coef[i] * scaled[i])
            double z = _params.intercept;
            for (int i = 0; i < scaled.Length && i < _params.coef.Count; i++)
            {
                z += scaled[i] * _params.coef[i];
            }

            // Sigmoid 转换为概率
            var probability = Sigmoid(z);

            return new EdemaPredictionResult
            {
                Probability = probability,
                IsPositive = probability >= 0.5, // 0.5 为二分类判定阈值
                Severity = GetSeverity(probability),
                SeverityDescription = GetSeverityDescription(probability)
            };
        }

        /// <summary>
        /// 根据概率区间返回风险等级标签
        /// </summary>
        /// <param name="probability">预测概率（0.0 ~ 1.0）</param>
        /// <returns>风险等级标签</returns>
        private static string GetSeverity(double probability)
        {
            // 四级风险分档阈值：0.25 / 0.50 / 0.75
            if (probability < 0.25) return "无明显水肿风险";
            if (probability < 0.50) return "轻度水肿风险";
            if (probability < 0.75) return "中度水肿风险";
            return "高度水肿风险";
        }

        /// <summary>
        /// 根据概率区间返回风险描述建议
        /// </summary>
        /// <param name="probability">预测概率（0.0 ~ 1.0）</param>
        /// <returns>风险描述和建议文本</returns>
        private static string GetSeverityDescription(double probability)
        {
            if (probability < 0.25) return "风险极低，基本无水肿迹象";
            if (probability < 0.50) return "风险较低，建议定期观察";
            if (probability < 0.75) return "风险中等，建议咨询医生";
            return "风险较高，建议立即就医检查";
        }
    }
}