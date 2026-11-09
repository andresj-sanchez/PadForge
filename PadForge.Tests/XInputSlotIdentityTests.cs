using PadForge.Engine;
using Xunit;

namespace PadForge.Tests
{
    public class XInputSlotIdentityTests
    {
        private const string BlePad =
            @"\\?\HID#{00001812-0000-1000-8000-00805f9b34fb}&Dev&VID_045e&PID_0b13&REV_0523&ac8ebd4a5666&IG_00#9&16b4ab9c&0&0000#{ec87f1e3-c13b-4100-b5f7-8b84d54260cb}";

        [Fact]
        public void ReadsTheAddressOfABluetoothLePad()
        {
            Assert.Equal("AC8EBD4A5666", XInputSlotIdentity.AddressFromInterfacePath(BlePad, 0x045E, 0x0B13));
        }

        [Fact]
        public void RefusesAnotherVendorOrProduct()
        {
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath(BlePad, 0x045E, 0x0B12));
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath(BlePad, 0x054C, 0x0B13));
        }

        [Fact]
        public void ReadsNothingFromAUsbPadOrAVirtualPad()
        {
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath(
                @"\\?\USB#VID_045E&PID_0B12&IG_00#7&2a1c3b0e&0&00#{ec87f1e3-c13b-4100-b5f7-8b84d54260cb}", 0x045E, 0x0B12));
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath(
                @"\\?\HID#HIDMAESTRO_VID_045E_PID_02FF&IG_00#1&d3dd7a1f&0&0000#{ec87f1e3-c13b-4100-b5f7-8b84d54260cb}", 0x045E, 0x02FF));
        }

        [Fact]
        public void ReadsNothingFromAnAllZeroAddressOrNoPath()
        {
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath(
                BlePad.Replace("ac8ebd4a5666", "000000000000"), 0x045E, 0x0B13));
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath(null, 0x045E, 0x0B13));
            Assert.Null(XInputSlotIdentity.AddressFromInterfacePath("", 0x045E, 0x0B13));
        }

        [Fact]
        public void AsksTheForkForTheSlotInTheSdlPath()
        {
            // Stands in for the fork, which serves four slots and answers
            // nothing for any other.
            uint asked = uint.MaxValue;
            XInputSlotIdentity.InterfacePathSeam = slot => { asked = slot; return slot < 4 ? BlePad : null; };
            try
            {
                Assert.Equal("AC8EBD4A5666", XInputSlotIdentity.TryGetBluetoothAddress("XInput#2", 0x045E, 0x0B13));
                Assert.Equal(2u, asked);
                Assert.Null(XInputSlotIdentity.TryGetBluetoothAddress(@"\\?\hid#vid_045e", 0x045E, 0x0B13));
                Assert.Null(XInputSlotIdentity.TryGetBluetoothAddress("XInput#7", 0x045E, 0x0B13));
            }
            finally { XInputSlotIdentity.InterfacePathSeam = null; }
        }
    }
}
