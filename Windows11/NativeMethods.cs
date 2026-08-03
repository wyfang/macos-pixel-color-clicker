using System.Runtime.InteropServices;

namespace PixelColorClicker;

internal static class NativeMethods
{
    internal const int VK_RETURN = 0x0D;
    internal const int VK_ESCAPE = 0x1B;
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint CLR_INVALID = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr windowHandle, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr deviceContext, int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    internal static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    internal static Color? ReadScreenPixel(POINT point)
        => ReadScreenRegion(point, 1);

    internal static Color? ReadScreenRegion(POINT center, int size)
    {
        int safeSize = Math.Clamp(size, 1, 10);
        int offset = safeSize / 2;
        IntPtr dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            long red = 0;
            long green = 0;
            long blue = 0;
            int count = 0;
            for (int y = 0; y < safeSize; y++)
            {
                for (int x = 0; x < safeSize; x++)
                {
                    uint value = GetPixel(dc, center.X - offset + x, center.Y - offset + y);
                    if (value == CLR_INVALID)
                    {
                        continue;
                    }
                    red += (int)(value & 0xFF);
                    green += (int)((value >> 8) & 0xFF);
                    blue += (int)((value >> 16) & 0xFF);
                    count++;
                }
            }
            return count == 0
                ? null
                : Color.FromArgb((int)(red / count), (int)(green / count), (int)(blue / count));
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, dc);
        }
    }

    internal static bool LeftClick(POINT point, bool moveCursor)
    {
        if (moveCursor && !SetCursorPos(point.X, point.Y))
        {
            return false;
        }

        INPUT[] inputs =
        [
            new INPUT
            {
                Type = INPUT_MOUSE,
                Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_LEFTDOWN } }
            },
            new INPUT
            {
                Type = INPUT_MOUSE,
                Data = new InputUnion { Mouse = new MOUSEINPUT { Flags = MOUSEEVENTF_LEFTUP } }
            }
        ];

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == (uint)inputs.Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        internal MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        internal int Dx;
        internal int Dy;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }
}
