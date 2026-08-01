using System.Runtime.InteropServices;

namespace PixelFlow.Runner.Automation;

/// <summary>
/// Keyboard SendInput helpers (Ctrl+A / Ctrl+V for paste-based Type steps).
/// </summary>
internal static class SendInputKeyboard
{
    public static void SelectAll() => Chord(NativeMethods.VkControl, NativeMethods.VkA);

    public static void Paste() => Chord(NativeMethods.VkControl, NativeMethods.VkV);

    private static void Chord(ushort modifierVk, ushort keyVk)
    {
        var inputs = new NativeMethods.Input[]
        {
            Key(modifierVk, keyUp: false),
            Key(keyVk, keyUp: false),
            Key(keyVk, keyUp: true),
            Key(modifierVk, keyUp: true),
        };

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"SendInput keyboard failed (sent {sent}/{inputs.Length}, error={Marshal.GetLastWin32Error()}).");
        }
    }

    private static NativeMethods.Input Key(ushort vk, bool keyUp) =>
        new()
        {
            Type = NativeMethods.InputKeyboard,
            U = new NativeMethods.InputUnion
            {
                Ki = new NativeMethods.KeyboardInput
                {
                    Vk = vk,
                    DwFlags = keyUp ? NativeMethods.KeyeventfKeyUp : 0,
                },
            },
        };
}
