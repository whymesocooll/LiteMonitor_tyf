using System;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 电池服务：专门处理电池状态识别、数值计算及模拟测试逻辑
    /// </summary>
    public static class BatteryService
    {
        // =========================================================
        // [Fix] 功耗读数缺失回退数据
        // 很多笔记本在放电时 Windows 的 BATTERY_STATUS.Rate 会报 0 或 unknown
        // (尤其刚拔掉适配器、或 EC 尚未完成测量时)，导致面板长时间显示 -0.00W。
        // 这里提供两层回退：
        //   1. 用 "Remaining Capacity" (mWh) 的时间差分反推放电功率；
        //   2. 保持最后一次有效读数一段时间。
        // =========================================================
        private static readonly object _batLock = new object();
        private static readonly List<(long tickMs, float capacityMWh)> _capHistory = new();
        private static readonly Dictionary<string, (long tickMs, float value)> _lastValid = new();
        private static bool _lastAcOnline = true;

        private const int HistoryWindowMs = 90_000;    // 容量历史保留窗口
        private const int EstimateMinSpanMs = 10_000;  // 差分最小时间跨度，保证分辨率
        private const int LastValidHoldMs = 600_000;   // 最后有效读数保持 10 分钟
        private const float PowerEpsilon = 0.05f;      // |读数| 低于此值视为缺失 (W)
        private const float CurrentEpsilon = 0.005f;   // |读数| 低于此值视为缺失 (A)

        /// <summary>
        /// 获取电池相关数值
        /// </summary>
        public static float? GetBatteryValue(string key, Dictionary<string, ISensor> sensorCache)
        {
            // 1. 模拟模式逻辑 (用于 UI 测试)
            bool simulateBattery = false; // 默认关闭，可根据需要开启
            if (simulateBattery)
            {
                return GetSimulatedValue(key);
            }

            // 2. 功率/电流：符号修正 + 读数缺失回退
            if (key == "BAT.Power" || key == "BAT.Current")
            {
                return GetFlowValue(key, sensorCache);
            }

            // 3. 其他数值 (百分比/电压等) 直接读取
            if (sensorCache.TryGetValue(key, out var sensor) && sensor.Value.HasValue)
            {
                return sensor.Value.Value;
            }

            return null;
        }

        /// <summary>
        /// 功率/电流取值：修正符号 (充电为正，放电为负)，读数缺失时回退
        /// </summary>
        private static float? GetFlowValue(string key, Dictionary<string, ISensor> sensorCache)
        {
            bool acOnline = MetricUtils.GetPowerStatus().AcOnline;
            float? raw = ReadSensorValue(sensorCache, key);

            lock (_batLock)
            {
                // 电源状态切换时清空容量历史，避免充电侧数据污染差分基线
                if (acOnline != _lastAcOnline)
                {
                    _lastAcOnline = acOnline;
                    _capHistory.Clear();
                }

                // 记录容量历史 (供差分估算)
                SampleCapacityHistory(sensorCache);

                // 2.1 传感器读数有效：按"插电为正、放电为负"强制赋符号
                if (raw.HasValue && Math.Abs(raw.Value) >= (key == "BAT.Power" ? PowerEpsilon : CurrentEpsilon))
                {
                    float val = acOnline ? Math.Abs(raw.Value) : -Math.Abs(raw.Value);
                    // 只记录放电侧读数，防止拔掉适配器后误用充电值兜底
                    if (!acOnline) _lastValid[key] = (Environment.TickCount64, val);
                    return val;
                }

                // 2.2 读数缺失 (0/unknown)：
                if (acOnline)
                {
                    // 插电时零功耗是合法状态 (充满/待机)，保持原有行为
                    return raw.HasValue ? Math.Abs(raw.Value) : (float?)null;
                }

                // 放电时读零基本是 EC 误报，进入回退：
                // 回退 1：剩余容量差分反推 (电流还需除以电压换算)
                float? estimated = EstimateDischargePower();
                if (estimated.HasValue && key == "BAT.Current")
                {
                    float? volts = ReadSensorValue(sensorCache, "BAT.Voltage");
                    estimated = volts is > 1f ? estimated.Value / volts.Value : null;
                }

                if (estimated.HasValue)
                {
                    float val = -estimated.Value; // 放电为负值
                    _lastValid[key] = (Environment.TickCount64, val);
                    return val;
                }

                // 回退 2：保持最后一次有效放电读数
                if (_lastValid.TryGetValue(key, out var last) &&
                    Environment.TickCount64 - last.tickMs <= LastValidHoldMs)
                {
                    return last.value;
                }

                // 都不可用 (如刚启动前几秒)：返回空值，避免误导性的 -0.00W
                return null;
            }
        }

        /// <summary>
        /// 采样剩余容量历史 (约每秒一个样本，保留最近 90 秒)
        /// </summary>
        private static void SampleCapacityHistory(Dictionary<string, ISensor> sensorCache)
        {
            float? cap = ReadSensorValue(sensorCache, "BAT.RemainCap");
            if (!cap.HasValue || cap.Value <= 0f) return;

            long now = Environment.TickCount64;
            if (_capHistory.Count == 0 || now - _capHistory[_capHistory.Count - 1].tickMs >= 500)
            {
                _capHistory.Add((now, cap.Value));
            }

            long cutoff = now - HistoryWindowMs;
            _capHistory.RemoveAll(s => s.tickMs < cutoff);
        }

        /// <summary>
        /// 用剩余容量差分反推放电功率 (W，正值)；跨度不足或噪声过大时返回空
        /// </summary>
        private static float? EstimateDischargePower()
        {
            if (_capHistory.Count < 2) return null;

            var newest = _capHistory[_capHistory.Count - 1];

            // 找跨度 >= 最小差分跨度的最旧基准点 (列表按时间有序)
            (long tickMs, float capacityMWh) basis = default;
            bool found = false;
            foreach (var s in _capHistory)
            {
                if (newest.tickMs - s.tickMs >= EstimateMinSpanMs) { basis = s; found = true; }
                else break;
            }
            if (!found) return null;

            double dtHours = (newest.tickMs - basis.tickMs) / 3600_000.0;
            if (dtHours <= 0) return null;

            // 放电时容量应减少，若增加视为噪声，放弃估算
            double deltaMWh = basis.capacityMWh - newest.capacityMWh;
            if (deltaMWh <= 0) return null;

            double watts = deltaMWh / dtHours / 1000.0;
            if (watts < 0.1 || watts > 250.0) return null; // 合理性校验
            return (float)watts;
        }

        private static float? ReadSensorValue(Dictionary<string, ISensor> sensorCache, string key)
        {
            if (sensorCache.TryGetValue(key, out var sensor) &&
                sensor.Value.HasValue &&
                !float.IsNaN(sensor.Value.Value))
            {
                return sensor.Value.Value;
            }
            return null;
        }

        private static float? GetSimulatedValue(string key)
        {
            var now = DateTime.Now;
            int sec = now.Second;

            // 前 30 秒：模拟 [高负载放电]，后 30 秒：模拟 [快充]
            bool isCharging = sec >= 30;
            
            float voltage = isCharging 
                ? 15.5f + ((sec - 30) * 0.05f) 
                : 16.8f - (sec * 0.06f);

            float power = isCharging ? -65.0f - (sec % 5) * 4.0f : 25.0f + (sec % 3) * 5.0f;
            float current = power / voltage;
            float percent = isCharging ? (sec - 30) * (100.0f / 30.0f) : 100.0f - (sec * (100.0f / 30.0f));

            return key switch
            {
                "BAT.Percent" => Math.Clamp(percent, 0f, 100f),
                "BAT.Power" => power,
                "BAT.Voltage" => voltage,
                "BAT.Current" => current,
                _ => null
            };
        }
    }
}
