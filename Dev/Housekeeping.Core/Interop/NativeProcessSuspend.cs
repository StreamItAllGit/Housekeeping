using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Housekeeping.Interop
{
    public static class NativeProcessSuspend
    {
        [DllImport("ntdll.dll", PreserveSig = false)]
        private static extern void NtSuspendProcess(IntPtr processHandle);

        [DllImport("ntdll.dll", PreserveSig = false)]
        private static extern void NtResumeProcess(IntPtr processHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;

        public static void PreventWindowsSleep(bool keepAwake)
        {
            try
            {
                if (keepAwake)
                    SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);
                else
                    SetThreadExecutionState(ES_CONTINUOUS);
            }
            catch (System.Exception __suppressedEx1) { System.Diagnostics.Debug.WriteLine($"[NativeProcessSuspend] Non-fatal error ignored: {__suppressedEx1.Message}"); }
        }

        public static void ExecutePostRunPowerAction(string actionMode)
        {
            try
            {
                if (string.Equals(actionMode, "Sleep", StringComparison.OrdinalIgnoreCase))
                {
                    SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false);
                }
                else if (string.Equals(actionMode, "Shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "shutdown.exe",
                        Arguments = "/s /t 60 /c \"Housekeeping completed. System will shut down in 60 seconds (run 'shutdown /a' to abort).\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch (System.Exception __suppressedEx2) { System.Diagnostics.Debug.WriteLine($"[NativeProcessSuspend] Non-fatal error ignored: {__suppressedEx2.Message}"); }
        }

        public static void SuspendProcessTree(int rootPid)
        {
            try
            {
                using var proc = Process.GetProcessById(rootPid);
                NtSuspendProcess(proc.Handle);
            }
            catch (System.Exception __suppressedEx3) { System.Diagnostics.Debug.WriteLine($"[NativeProcessSuspend] Non-fatal error ignored: {__suppressedEx3.Message}"); }

            foreach (var proc in Process.GetProcessesByName("HandBrakeCLI"))
            {
                try { NtSuspendProcess(proc.Handle); } catch (System.Exception __suppressedEx4) { System.Diagnostics.Debug.WriteLine($"[NativeProcessSuspend] Non-fatal error ignored: {__suppressedEx4.Message}"); }
            }
        }

        public static void ResumeProcessTree(int rootPid)
        {
            try
            {
                using var proc = Process.GetProcessById(rootPid);
                NtResumeProcess(proc.Handle);
            }
            catch (System.Exception __suppressedEx5) { System.Diagnostics.Debug.WriteLine($"[NativeProcessSuspend] Non-fatal error ignored: {__suppressedEx5.Message}"); }

            foreach (var proc in Process.GetProcessesByName("HandBrakeCLI"))
            {
                try { NtResumeProcess(proc.Handle); } catch (System.Exception __suppressedEx6) { System.Diagnostics.Debug.WriteLine($"[NativeProcessSuspend] Non-fatal error ignored: {__suppressedEx6.Message}"); }
            }
        }
    }
}
