#define _WIN32
//#define LINUX
using System;
using System.Runtime.InteropServices;

namespace Crypto.IO.Hwmon
{
    public class Adl : IHwmon
    {
        const int ADL_MAX_PATH = 256;
        const string LibraryName = "atiadlxx.dll";

        enum Result : int
        {
            OK = 0,
        }

        IntPtr ADL_Main_Memory_Alloc(int size) => Marshal.AllocHGlobal(size);

        delegate IntPtr AllocCallback(int size);

        [DllImport(LibraryName, EntryPoint = "ADL_Main_Control_Create")] static extern Result MainControlCreate(AllocCallback mallocCallback, int a);
        [DllImport(LibraryName, EntryPoint = "ADL_Adapter_NumberOfAdapters_Get")] static extern Result AdapterNumberOfAdapters(ref int a);
        [DllImport(LibraryName, EntryPoint = "ADL_Adapter_AdapterInfo_Get")] static extern Result AdapterAdapterInfoGet([MarshalAs(UnmanagedType.LPArray)] AdapterInfo[] devs, int a);
        [DllImport(LibraryName, EntryPoint = "ADL_Adapter_ID_Get")] static extern Result AdapterAdapterIdGet(int a, ref int b);
        [DllImport(LibraryName, EntryPoint = "ADL_Overdrive5_Temperature_Get")] static extern Result Overdrive5TemperatureGet(int a, int b, ref ADLTemperature c);
        [DllImport(LibraryName, EntryPoint = "ADL_Overdrive5_FanSpeed_Get")] static extern Result Overdrive5FanSpeedGet(int a, int b, ref ADLFanSpeedValue c);
        [DllImport(LibraryName, EntryPoint = "ADL_Main_Control_Refresh")] static extern Result MainControlRefresh();
        [DllImport(LibraryName, EntryPoint = "ADL_Main_Control_Destroy")] static extern Result MainControlDestroy();
        [DllImport(LibraryName, EntryPoint = "ADL2_Main_Control_Create")] static extern Result _2MainControlCreate(AllocCallback mallocCallback, int a, ref IntPtr handle);
        [DllImport(LibraryName, EntryPoint = "ADL_Main_Control_Destroy")] static extern Result _2MainControlDestroy(IntPtr handle);
        [DllImport(LibraryName, EntryPoint = "ADL2_Overdrive6_CurrentPower_Get")] static extern Result _2Overdrive6CurrentPowerGet(IntPtr handle, int a, int b, ref int c);
        [DllImport(LibraryName, EntryPoint = "ADL2_Main_Control_Refresh")] static extern Result _2MainControlRefresh(IntPtr handle);

        /// <summary>
        /// AdapterInfo
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct AdapterInfo
        {
            /// <summary>
            /// Size of the structure.
            /// </summary>
            public int Size;
            /// <summary>
            /// The ADL index handle. One GPU may be associated with one or two index handles
            /// </summary>
            public int AdapterIndex;
            /// <summary>
            /// The unique device ID associated with this adapter.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string UDID;
            /// <summary>
            /// The BUS number associated with this adapter.
            /// </summary>
            public int BusNumber;
            /// <summary>
            /// The driver number associated with this adapter.
            /// </summary>
            public int DeviceNumber;
            /// <summary>
            /// The function number.
            /// </summary>
            public int FunctionNumber;
            /// <summary>
            /// The vendor ID associated with this adapter.
            /// </summary>
            public int VendorID;
            /// <summary>
            /// Adapter name.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string AdapterName;
            /// <summary>
            /// Display name. For example, "\\Display0" for Windows or ":0:0" for Linux.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DisplayName;
            /// <summary>
            /// Present or not; 1 if present and 0 if not present.It the logical adapter is present, the display name such as \\.\Display1 can be found from OS
            /// </summary>
            public int Present;
#if _WIN32
            /// <summary>
            /// Exist or not; 1 is exist and 0 is not present.
            /// </summary>
            public int Exist;
            /// <summary>
            /// Driver registry path.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DriverPath;
            /// <summary>
            /// Driver registry path Ext for.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DriverPathExt;
            /// <summary>
            /// PNP string from Windows.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string PNPString;
            /// <summary>
            /// It is generated from EnumDisplayDevices.
            /// </summary>
            public int OSDisplayIndex;
#endif
#if LINUX
            /// <summary>
            /// Internal X screen number from GPUMapInfo (DEPRICATED use XScreenInfo)
            /// </summary>
            public int XScreenNum;
            /// <summary>
            /// Internal driver index from GPUMapInfo
            /// </summary>
            public int DrvIndex;
            /// <summary>
            /// Internal x config file screen identifier name. Use XScreenInfo instead.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string XScreenConfigName;
#endif
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLTemperature
        {
            // Must be set to the size of the structure
            public int Size;
            // Temperature in millidegrees Celsius.
            public int Temperature;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ADLFanSpeedValue
        {
            /// <summary>
            /// Must be set to the size of the structure
            /// </summary>
            public int Size;
            /// <summary>
            /// Possible valies: \ref ADL_DL_FANCTRL_SPEED_TYPE_PERCENT or \ref ADL_DL_FANCTRL_SPEED_TYPE_RPM
            /// </summary>
            public int SpeedType;
            /// <summary>
            /// Fan speed value
            /// </summary>
            public int FanSpeed;
            /// <summary>
            /// The only flag for now is: \ref ADL_DL_FANCTRL_FLAG_USER_DEFINED_SPEED
            /// </summary>
            public int Flags;
        }

        int _gpuCount;
        int _gpuCountLog;
        int[] _deviceIds;
        AdapterInfo[] _devices;
        IntPtr _context = IntPtr.Zero;

        public Adl()
        {
            MainControlCreate(ADL_Main_Memory_Alloc, 1);
            MainControlRefresh();
            _2MainControlCreate(ADL_Main_Memory_Alloc, 1, ref _context);
            _2MainControlRefresh(_context);
            var logicalGpuCount = 0;
            AdapterNumberOfAdapters(ref logicalGpuCount);
            _deviceIds = new int[logicalGpuCount];
            _gpuCount = 0;
            if (logicalGpuCount == 0)
                return;
            var lastAdapterId = 0;
            _gpuCountLog = logicalGpuCount;
            _devices = new AdapterInfo[logicalGpuCount];
            _devices[0].Size = Marshal.SizeOf<AdapterInfo>();
            if (AdapterAdapterInfoGet(_devices, Marshal.SizeOf<AdapterInfo>() * logicalGpuCount) != Result.OK)
                throw new InvalidOperationException("Failed to obtain using adlAdapterAdapterInfoGet(). AMD hardware monitoring disabled");
            for (var i = 0; i < logicalGpuCount; i++)
            {
                var adapterIndex = _devices[i].AdapterIndex;
                var adapterId = 0;
                if (AdapterAdapterIdGet(adapterIndex, ref adapterId) != Result.OK)
                    continue;
                _deviceIds[_gpuCount] = adapterIndex;
                if (adapterId == lastAdapterId)
                    continue;
                lastAdapterId = adapterId;
                _gpuCount++;
            }
        }

        public void Dispose()
        {
            MainControlDestroy();
            _2MainControlDestroy(_context);
        }

        public int GpuCount => _gpuCount;

        public string GpuName(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            return _devices[_deviceIds[index]].AdapterName;
        }

        public string GetPciId(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            return $"{0,04:x}:{_devices[_deviceIds[index]].BusNumber,02:x}:{_devices[_deviceIds[index]].DeviceNumber,02:x}";
            //    std::ostringstream oss;
            //    std::string uniqueId;
            //    oss << std::setfill('0') << std::setw(2) << std::hex
            //        << (unsigned int)adlh->devs[adlh->phys_logi_device_id[i]].iBusNumber << ":"
            //        << std::setw(2)
            //        << (unsigned int)(adlh->devs[adlh->phys_logi_device_id[i]].iDeviceNumber)
            //        << ".0";
            //uniqueId = oss.str();
        }

        public uint? GetTempC(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var temperature = new ADLTemperature();
            if (Overdrive5TemperatureGet(_deviceIds[index], 0, ref temperature) != Result.OK)
                return null;
            return (uint)(temperature.Temperature / 1000);
        }

        public uint? GetFanpcnt(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var fan = new ADLFanSpeedValue { SpeedType = 1 };
            if (Overdrive5FanSpeedGet(_deviceIds[index], 0, ref fan) != Result.OK)
                return null;
            return (uint)fan.FanSpeed;
        }

        public uint? GetPowerUsage(int index)
        {
            if (index < 0 || index >= _gpuCount)
                return null;
            var power = 0;
            if (_2Overdrive6CurrentPowerGet(_context, _deviceIds[index], 0, ref power) != Result.OK)
                return null;
            return (uint)(power * 3.90625);
        }
    }
}
