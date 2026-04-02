using System;
using System.Runtime.InteropServices;

namespace PocketMC.Desktop.Utils
{
    public static class MemoryHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public static ulong GetTotalPhysicalMemoryMb()
        {
            // fallback to GC info if Windows API fails or is unavailable on cross-platform
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var memStatus = new MEMORYSTATUSEX();
                    if (GlobalMemoryStatusEx(memStatus))
                    {
                        return memStatus.ullTotalPhys / (1024 * 1024);
                    }
                }
            }
            catch
            {
                // Ignore exception, fallback
            }

            // Fallback for non-windows or if restricted
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            return (ulong)gcMemoryInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        }
    }
}
