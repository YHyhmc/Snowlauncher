using System.Runtime.InteropServices;

namespace WPCL.Interop;

internal static class DesktopAcrylic
{
    private const int DwmWindowAttributeSystemBackdropType = 38;
    private const int DesktopAcrylicBackdrop = 3;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    public static bool TryApply(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            return false;
        }

        var backdrop = DesktopAcrylicBackdrop;
        return DwmSetWindowAttribute(
            hwnd,
            DwmWindowAttributeSystemBackdropType,
            ref backdrop,
            sizeof(int)) >= 0;
    }
}
