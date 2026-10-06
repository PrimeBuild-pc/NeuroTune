using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class ResponsePipes
{
    internal static void PreventInheritance()
    {
        foreach (var id in new[] { -10, -11, -12 }) // STD_INPUT_HANDLE, STD_OUTPUT_HANDLE, STD_ERROR_HANDLE.
        {
            var handle = GetStdHandle(id);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
            if (!SetHandleInformation(handle, 1, 0)) // Clear HANDLE_FLAG_INHERIT; the Agent retains its own I/O.
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not isolate the Agent response pipes.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int id);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
