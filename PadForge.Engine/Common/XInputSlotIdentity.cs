using System;
using System.Runtime.InteropServices;

namespace PadForge.Engine
{
    /// <summary>
    /// The Bluetooth address of the pad behind an SDL "XInput#N" joystick,
    /// read from OpenXInput's identity call.
    ///
    /// <para>SDL's XInput driver gives such a pad no serial, so two pads of
    /// one model (two Xbox Series pads on Bluetooth LE) can only be told
    /// apart by their slot number, which moves whenever they reconnect in
    /// another order. OpenXInput knows which device interface serves each
    /// slot (OpenXInputGetDeviceIdentityV1, OpenXinput.cpp), and a Bluetooth
    /// LE pad's interface names its address in its device id:
    /// \\?\HID#{00001812-...}&amp;Dev&amp;VID_045e&amp;PID_0b13&amp;REV_0523&amp;ac8ebd4a5666&amp;IG_00#....
    /// With that address as the serial, the pad's identity no longer
    /// depends on the slot, and HidHide's serial-scoped sweep can tell the
    /// two pads apart.</para>
    ///
    /// <para>Only meaningful while SDL reads through the fork
    /// (<see cref="OpenXInputLoader.ForkIsActive"/>): Microsoft's XInput
    /// numbers its slots differently, so a slot index from SDL would name
    /// another pad.</para>
    /// </summary>
    public static class XInputSlotIdentity
    {
        /// <summary>Test seam: answers in place of the fork when set.</summary>
        internal static Func<uint, string> InterfacePathSeam;

        /// <summary>The address as twelve upper-case hex digits, or null when
        /// the slot is not a Bluetooth LE pad of this vendor and product id,
        /// or the fork cannot say.</summary>
        public static string TryGetBluetoothAddress(string sdlDevicePath, ushort vid, ushort pid)
        {
            if (!OpenXInputLoader.ForkIsActive && InterfacePathSeam == null) return null;
            int slot = ParseSlot(sdlDevicePath);
            if (slot < 0) return null;
            string path = InterfacePathSeam != null ? InterfacePathSeam((uint)slot) : InterfacePath((uint)slot);
            return AddressFromInterfacePath(path, vid, pid);
        }

        /// <summary>Pure text half: the address in a Bluetooth LE HID
        /// interface path whose device id carries this vendor and product
        /// id, else null.</summary>
        internal static string AddressFromInterfacePath(string path, ushort vid, ushort pid)
        {
            if (string.IsNullOrEmpty(path)) return null;
            // The device id is the segment after "HID#" up to the next '#'.
            int start = path.IndexOf(@"HID#", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            start += 4;
            int end = path.IndexOf('#', start);
            if (end < 0) return null;
            string deviceId = path.Substring(start, end - start);
            // Bluetooth LE HID-over-GATT service only.
            if (!deviceId.StartsWith("{00001812-", StringComparison.OrdinalIgnoreCase)) return null;
            if (deviceId.IndexOf($"VID_{vid:X4}", StringComparison.OrdinalIgnoreCase) < 0
                || deviceId.IndexOf($"PID_{pid:X4}", StringComparison.OrdinalIgnoreCase) < 0)
                return null;
            foreach (string token in deviceId.Split('&'))
            {
                if (token.Length != 12) continue;
                bool hex = true, nonZero = false;
                foreach (char c in token)
                {
                    if (!Uri.IsHexDigit(c)) { hex = false; break; }
                    if (c != '0') nonZero = true;
                }
                if (hex && nonZero) return token.ToUpperInvariant();
            }
            return null;
        }

        private static int ParseSlot(string sdlDevicePath)
        {
            const string prefix = "XInput#";
            if (string.IsNullOrEmpty(sdlDevicePath)
                || !sdlDevicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;
            return int.TryParse(sdlDevicePath.AsSpan(prefix.Length), out int slot) && slot >= 0 && slot < 4 ? slot : -1;
        }

        private static string InterfacePath(uint slot)
        {
            try
            {
                var identity = new Identity { cbSize = (uint)Marshal.SizeOf<Identity>() };
                var buffer = new char[512];
                uint rc = OpenXInputGetDeviceIdentityV1(slot, ref identity, buffer, (uint)buffer.Length);
                if (rc == ErrorInsufficientBuffer && identity.requiredCch > buffer.Length && identity.requiredCch <= 32768)
                {
                    buffer = new char[identity.requiredCch];
                    rc = OpenXInputGetDeviceIdentityV1(slot, ref identity, buffer, (uint)buffer.Length);
                }
                if (rc == 0 && identity.requiredCch > 1 && identity.requiredCch <= buffer.Length)
                    return new string(buffer, 0, (int)identity.requiredCch - 1);
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            return null;
        }

        private const uint ErrorInsufficientBuffer = 122;

        // OPENXINPUT_DEVICE_IDENTITY_V1 (OpenXinput.h): 24 bytes, generation at 8.
        [StructLayout(LayoutKind.Sequential)]
        private struct Identity
        {
            public uint cbSize;
            public uint requiredCch;
            public ulong generation;
            public uint interfaceIndex;
            public uint interfaceCount;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "OpenXInputGetDeviceIdentityV1", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint OpenXInputGetDeviceIdentityV1(uint userIndex, ref Identity identity,
            [Out] char[] path, uint capacityCch);
    }
}
