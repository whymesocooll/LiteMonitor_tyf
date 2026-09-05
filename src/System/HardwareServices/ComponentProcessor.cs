using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LiteMonitor.src.Core;

namespace LiteMonitor.src.SystemServices
{
    /// <summary>
    /// 组件处理器：负责 CPU 和 GPU 的复杂数值加工（聚合、修正、过滤）
    /// </summary>
    public class ComponentProcessor
    {
        private readonly IComputer _computer;
        private readonly Settings _cfg;
        private readonly SensorMap _sensorMap;

        private List<ISensor>? _cpuLoadSensorsCache = null;
        private List<ISensor>? _cpuTempSensorsCache = null;

        public ComponentProcessor(IComputer computer, Settings cfg, SensorMap sensorMap)
        {
            _computer = computer;
            _cfg = cfg;
            _sensorMap = sensorMap;
        }

        public void ClearCache()
        {
            _cpuLoadSensorsCache = null;
            _cpuTempSensorsCache = null;
        }

        /// <summary>
        /// 获取 CPU 负载 (多核平均)
        /// </summary>
        public float? GetCpuLoad()
        {
            if (_cpuLoadSensorsCache == null)
            {
                var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpu != null)
                {
                    _cpuLoadSensorsCache = new List<ISensor>();
                    foreach (var s in cpu.Sensors)
                    {
                        if (s.SensorType != SensorType.Load) continue;
                        if (SensorMap.Has(s.Name, "Core") && SensorMap.Has(s.Name, "#") && 
                            !SensorMap.Has(s.Name, "Total") && !SensorMap.Has(s.Name, "SOC") && 
                            !SensorMap.Has(s.Name, "Max") && !SensorMap.Has(s.Name, "Average"))
                        {
                            _cpuLoadSensorsCache.Add(s);
                        }
                    }
                }
            }

            if (_cpuLoadSensorsCache != null && _cpuLoadSensorsCache.Count > 0)
            {
                double totalLoad = 0;
                int coreCount = 0;
                foreach (var s in _cpuLoadSensorsCache)
                {
                    if (s.Value.HasValue) { totalLoad += s.Value.Value; coreCount++; }
                }
                if (coreCount > 0) return (float)(totalLoad / coreCount);
            }
            return null;
        }

        /// <summary>
        /// 获取 CPU 温度 (核心最大值)
        /// </summary>
        public float? GetCpuTemp()
        {
            if (_cpuTempSensorsCache == null)
            {
                var cpuT = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
                if (cpuT != null)
                {
                    _cpuTempSensorsCache = new List<ISensor>();
                    foreach (var s in cpuT.Sensors)
                    {
                        if (s.SensorType == SensorType.Temperature && 
                            !SensorMap.Has(s.Name, "Distance") && !SensorMap.Has(s.Name, "Average") && !SensorMap.Has(s.Name, "Max"))
                        {
                            _cpuTempSensorsCache.Add(s);
                        }
                    }
                }
            }

            if (_cpuTempSensorsCache != null && _cpuTempSensorsCache.Count > 0)
            {
                float maxTemp = -1000f;
                bool found = false;
                foreach (var s in _cpuTempSensorsCache)
                {
                    if (s.Value.HasValue && s.Value.Value > 0)
                    {
                        if (s.Value.Value > maxTemp) { maxTemp = s.Value.Value; found = true; }
                    }
                }
                if (found) return maxTemp;
            }
            return null;
        }

        /// <summary>
        /// 获取复合数值 (频率、功耗等带修正或过滤逻辑的数值)
        /// </summary>
        public float? GetCompositeValue(string key, Dictionary<string, ISensor> sensorCache)
        {
            if (key == "SYS.Power")
            {
                return GetSystemPower(sensorCache);
            }

            if (key == "CPU.Clock")
            {
                if (_sensorMap.CpuCoreCache.Count == 0) return null;
                double sum = 0; int count = 0; float maxRaw = 0;
                
                // Zen 5 频率修正逻辑
                float correctionFactor = 1.0f;
                if (_sensorMap.CpuBusSpeedSensor != null && _sensorMap.CpuBusSpeedSensor.Value.HasValue)
                {
                    float bus = _sensorMap.CpuBusSpeedSensor.Value.Value;
                    if (bus > 1.0f && bus < 20.0f) 
                    { 
                        float factor = 100.0f / bus; 
                        if (factor > 2.0f && factor < 10.0f) correctionFactor = factor; 
                    }
                }

                foreach (var core in _sensorMap.CpuCoreCache)
                {
                    if (core.Clock == null || !core.Clock.Value.HasValue) continue;
                    float clk = core.Clock.Value.Value * correctionFactor;
                    if (clk > maxRaw) maxRaw = clk;
                    if (clk > 400f) { sum += clk; count++; }
                }
                if (maxRaw > 0) _cfg.UpdateMaxRecord(key, maxRaw);
                if (count > 0) return (float)(sum / count);
                return maxRaw;
            }

            if (key == "CPU.Power")
            {
                if (sensorCache.TryGetValue("CPU.Power", out var s) && s.Value.HasValue) 
                { 
                    // 熔断保护：过滤异常高功耗
                    if (s.Value.Value > 600.0f) return null;
                    _cfg.UpdateMaxRecord(key, s.Value.Value); 
                    return s.Value.Value; 
                }
                return null;
            }

            if (key.StartsWith("GPU"))
            {
                var gpu = _sensorMap.CachedGpu;
                if (gpu == null) return null;
                if (key == "GPU.Clock")
                {
                    var s = gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Clock && (SensorMap.Has(x.Name, "graphics") || SensorMap.Has(x.Name, "core") || SensorMap.Has(x.Name, "shader")));
                    if (s != null && s.Value.HasValue) 
                    { 
                        float val = s.Value.Value; 
                        if (val > 6000.0f) return null; 
                        _cfg.UpdateMaxRecord(key, val); 
                        return val; 
                    }
                }
                else if (key == "GPU.Power")
                {
                    var s = gpu.Sensors.FirstOrDefault(x => x.SensorType == SensorType.Power && (SensorMap.Has(x.Name, "package") || SensorMap.Has(x.Name, "ppt") || SensorMap.Has(x.Name, "board") || SensorMap.Has(x.Name, "core") || SensorMap.Has(x.Name, "gpu")));
                    if (s != null && s.Value.HasValue) 
                    { 
                        float val = s.Value.Value; 
                        if (val > 1200.0f) return null; 
                        _cfg.UpdateMaxRecord(key, val); 
                        return val; 
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// [SYS.Power] 整机功耗估算：权威源优先，尽量把能读到的功耗都纳入。
        /// 优先级：
        ///   1. 电池放电中 → 放电功率 = 整机直流功耗 (含屏幕/主板/内存等，最接近真实整机功耗)；
        ///   2. PSU 总功耗传感器 (HID 电源，台式机) → 本身就是整机功耗；
        ///   3. 组件求和 → CPU.Power + GPU.Power + 其它可读功耗传感器 (主板/内存/存储等)。
        /// 避免重复计数：CPU/GPU/电池功耗不参与组件求和，电池只在放电时作为权威源。
        /// </summary>
        private float? GetSystemPower(Dictionary<string, ISensor> sensorCache)
        {
            // 1. 电池放电中：放电功率即整机功耗 (BAT.Power 约定: 放电为负, 插电为正)
            if (!MetricUtils.GetPowerStatus().AcOnline)
            {
                float? bat = BatteryService.GetBatteryValue("BAT.Power", sensorCache);
                if (bat.HasValue && bat.Value < 0f && float.IsFinite(bat.Value))
                {
                    float batPower = -bat.Value;
                    // 合理性区间 (0.5W ~ 500W)，防止 EC 误报污染整机功耗
                    if (batPower >= 0.5f && batPower <= 500f)
                    {
                        return RecordSystemPower(batPower);
                    }
                }
            }

            // 2. PSU 总功耗传感器 (台式机 HID 电源)：本身就是整机功耗
            var psu = _sensorMap.PsuTotalPowerSensor;
            if (psu != null && psu.Value.HasValue && float.IsFinite(psu.Value.Value) &&
                psu.Value.Value >= 1f && psu.Value.Value <= 1000f)
            {
                return RecordSystemPower(psu.Value.Value);
            }

            // 3. 组件求和兜底
            float? fallback = SumComponentPower(sensorCache);
            return fallback.HasValue ? RecordSystemPower(fallback.Value) : null;
        }

        /// <summary>
        /// [SYS.Power] 组件求和：CPU + GPU + 其它可读功耗传感器。
        /// 只累加有效值；全部缺失时返回 null (而非 0)，与现有缺失语义一致。
        /// </summary>
        private float? SumComponentPower(Dictionary<string, ISensor> sensorCache)
        {
            float total = 0f;
            bool has = false;

            bool TryAdd(float? value, float max)
            {
                if (!IsValidComponentPower(value, max)) return false;
                total += value!.Value;
                has = true;
                return true;
            }

            TryAdd(GetCompositeValue("CPU.Power", sensorCache), 600f);
            TryAdd(GetCompositeValue("GPU.Power", sensorCache), 1200f);

            // 同一硬件已在 SensorMap 中选出单个聚合功耗，避免重复计数。
            foreach (var sensor in _sensorMap.SystemPowerSensors)
            {
                TryAdd(sensor.Value, 200f);
            }

            return has && float.IsFinite(total) && total <= 1000f ? total : null;
        }

        private float RecordSystemPower(float value)
        {
            _cfg.UpdateMaxRecord("SYS.Power", value);
            return value;
        }

        private static bool IsValidComponentPower(float? value, float max)
        {
            return value.HasValue && float.IsFinite(value.Value) && value.Value >= 0f && value.Value <= max;
        }
    }
}
