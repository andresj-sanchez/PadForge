using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PadForge.Engine
{
    /// <summary>
    /// What OpenXInput says about the device behind an XInput slot: the
    /// interface it serves the slot from, and for a Bluetooth LE pad the
    /// pad's address, read from that interface's device id.
    ///
    /// <para>SDL's XInput driver gives an "XInput#N" joystick no serial, so
    /// two pads of one model (two Xbox Series pads on Bluetooth LE) could
    /// only be told apart by slot number, which moves whenever they
    /// reconnect in another order (SdlDeviceWrapper.BuildInstanceGuid).
    /// OpenXInput knows which interface serves each slot
    /// (OpenXInputGetDeviceIdentityV1, OpenXinput.cpp:4103), and a Bluetooth
    /// LE pad's interface names its address in its device id:
    /// \\?\HID#{00001812-...}&amp;Dev&amp;VID_045e&amp;PID_0b13&amp;REV_0523&amp;ac8ebd4a5666&amp;IG_00#....
    /// With that address as the serial, the pad's identity no longer depends
    /// on the slot, and HidHide's serial-scoped sweep
    /// (InputService.SelectHidHideSweepNodes) can tell the two pads apart.</para>
    ///
    /// <para>A slot index from SDL names the same pad here only while SDL
    /// reads through the fork (<see cref="OpenXInputLoader.ForkIsActive"/>).
    /// Microsoft's XInput gives PadForge's own virtual pads slots too, so its
    /// numbering differs.</para>
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
            string path = InterfacePathSeam != null
                ? InterfacePathSeam((uint)slot)
                : InterfacePath((uint)slot, out _);
            return AddressFromInterfacePath(path, vid, pid);
        }

        /// <summary>The interface OpenXInput serves an XInput slot from, or
        /// null. <paramref name="final"/> says whether asking again later
        /// can change a null answer: Microsoft's xinput1_4.dll has no such
        /// export, and the fork answers ERROR_NOT_SUPPORTED while an override
        /// serves the state, which are final.
        ///
        /// <para>The identity call does no enumeration or device I/O and
        /// answers ERROR_BUSY instead of waiting on its lock
        /// (OpenXinput.h:476-478). A pad opened while another thread holds
        /// that lock would read no address and fall back to its slot identity
        /// for the session, so a busy answer is asked again a few times
        /// first.</para></summary>
        public static string InterfacePath(uint slot, out bool final)
        {
            final = true;
            try
            {
                uint rc = ERROR_BUSY;
                for (int attempt = 0; attempt < BUSY_ATTEMPTS && rc == ERROR_BUSY; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(1);
                    var identity = new OpenXInputIdentity { cbSize = (uint)Marshal.SizeOf<OpenXInputIdentity>() };
                    var buffer = new char[512];
                    rc = OpenXInputGetDeviceIdentityV1(slot, ref identity, buffer, (uint)buffer.Length);
                    if (rc == ERROR_INSUFFICIENT_BUFFER && identity.requiredCch > buffer.Length && identity.requiredCch <= 32768)
                    {
                        buffer = new char[identity.requiredCch];
                        rc = OpenXInputGetDeviceIdentityV1(slot, ref identity, buffer, (uint)buffer.Length);
                    }
                    if (rc == ERROR_SUCCESS && identity.requiredCch > 1 && identity.requiredCch <= buffer.Length)
                        return new string(buffer, 0, (int)identity.requiredCch - 1);
                }
                final = rc == ERROR_NOT_SUPPORTED;
                return null;
            }
            catch (EntryPointNotFoundException) { return null; }
            catch (DllNotFoundException) { return null; }
        }

        /// <summary>Pure text half: the address in a Bluetooth LE HID
        /// interface path whose device id carries this vendor and product
        /// id, else null. The device id is the segment after "HID#" up to the
        /// next '#'; its address is the one twelve-hex-digit '&amp;' token.
        /// A USB pad's interface, and HIDMaestro's, carry none.</summary>
        internal static string AddressFromInterfacePath(string path, ushort vid, ushort pid)
        {
            if (string.IsNullOrEmpty(path)) return null;
            int start = path.IndexOf(@"HID#", StringComparison.OrdinalIgnoreCase);
            if (start < 0) return null;
            start += 4;
            int end = path.IndexOf('#', start);
            if (end < 0) return null;
            string deviceId = path.Substring(start, end - start);
            // HID over GATT (service 0x1812), the Bluetooth LE transport.
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

        /// <summary>The slot in an SDL "XInput#N" path, else -1. The range is
        /// the fork's to judge: it answers ERROR_INVALID_PARAMETER for a slot
        /// it does not serve.</summary>
        private static int ParseSlot(string sdlDevicePath)
        {
            const string prefix = "XInput#";
            if (string.IsNullOrEmpty(sdlDevicePath)
                || !sdlDevicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;
            return int.TryParse(sdlDevicePath.AsSpan(prefix.Length), out int slot) && slot >= 0 ? slot : -1;
        }

        // ── Native ──

        /// <summary>OPENXINPUT_DEVICE_IDENTITY_V1 (OpenXinput.h:463-470): 24
        /// bytes, generation at offset 8.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct OpenXInputIdentity
        {
            public uint cbSize;
            public uint requiredCch;
            public ulong generation;
            public uint interfaceIndex;
            public uint interfaceCount;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "OpenXInputGetDeviceIdentityV1", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint OpenXInputGetDeviceIdentityV1(uint userIndex, ref OpenXInputIdentity identity,
            [Out] char[] path, uint capacityCch);

        private const int BUSY_ATTEMPTS = 5;
        private const uint ERROR_SUCCESS = 0;
        private const uint ERROR_NOT_SUPPORTED = 50;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint ERROR_BUSY = 170;
    }
}
