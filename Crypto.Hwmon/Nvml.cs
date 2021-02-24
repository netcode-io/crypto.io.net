#define _WIN32
//#define LINUX
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CryptoPool.IO.Hwmon
{
    public class Nvml : IHwmon
    {
        const string LibraryName = "nvml.dll";

        enum Result : int
        {
            OK = 0,
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr LoadLibrary(string fileName);
        [DllImport(LibraryName, EntryPoint = "nvmlInit")] static extern Result Init();
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetCount_v2")] static extern Result DeviceGetCount(ref int count);
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")] static extern Result DeviceGetHandleByIndex(int index, ref IntPtr device);
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetPciInfo")] static extern Result DeviceGetPciInfo(IntPtr device, ref PciInfo_t info);
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetName")] static extern Result DeviceGetName(IntPtr device, StringBuilder name, int nameSize);
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetTemperature")] static extern Result DeviceGetTemperature(IntPtr device, int a, ref uint b);
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetFanSpeed")] static extern Result DeviceGetFanSpeed(IntPtr device, ref uint a);
        [DllImport(LibraryName, EntryPoint = "nvmlDeviceGetPowerUsage")] static extern Result DeviceGetPowerUsage(IntPtr device, ref uint a);
        [DllImport(LibraryName, EntryPoint = "nvmlShutdown")] static extern Result Shutdown();

        /// <summary>
        /// PciInfo_t
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct PciInfo_t
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string BusId;
            public uint Domain;
            public uint Bus;
            public uint Device;
            public uint PciDeviceId; /* combined device and vendor id */
            public uint PciSubsystemId;
            public uint Res0; /* NVML internal use only */
            public uint Res1;
            public uint Res2;
            public uint Res3;
        }

        int _gpuCount;
        uint[] _pciDomainIds;
        uint[] _pciBusIds;
        uint[] _pciDeviceIds;
        IntPtr[] _devices;

        static Nvml() =>
            LoadLibrary(Environment.ExpandEnvironmentVariables("%PROGRAMFILES%/NVIDIA Corporation/NVSMI/nvml.dll"));

        public Nvml()
        {
            Init();
            DeviceGetCount(ref _gpuCount);
            _devices = new IntPtr[_gpuCount];
            _pciDomainIds = new uint[_gpuCount];
            _pciBusIds = new uint[_gpuCount];
            _pciDeviceIds = new uint[_gpuCount];
            for (var i = 0; i < _gpuCount; i++)
            {
                DeviceGetHandleByIndex(i, ref _devices[i]);
                var info = new PciInfo_t();
                DeviceGetPciInfo(_devices[i], ref info);
                _pciDomainIds[i] = info.Domain;
                _pciBusIds[i] = info.Bus;
                _pciDeviceIds[i] = info.Device;
            }
        }

        public void Dispose() => Shutdown();

        public int GpuCount => _gpuCount;

        public string GpuName(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var name = new StringBuilder(256);
            if (DeviceGetName(_devices[index], name, name.Capacity) != Result.OK)
                return null;
            return name.ToString();
        }

        public uint? GetTempC(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var tempC = 0U;
            if (DeviceGetTemperature(_devices[index], 0, ref tempC) != Result.OK)
                return null;
            return tempC;
        }

        public uint? GetFanpcnt(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var fanpcnt = 0U;
            if (DeviceGetFanSpeed(_devices[index], ref fanpcnt) != Result.OK)
                return null;
            return fanpcnt;
        }

        public uint? GetPowerUsage(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var milliwatts = 0U;
            if (DeviceGetPowerUsage(_devices[index], ref milliwatts) != Result.OK)
                return null;
            return milliwatts;
        }
    }
}
