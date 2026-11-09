using System;
using System.Runtime.InteropServices;

namespace PadForge.Engine
{
    /// <summary>
    /// Makes PadForge's OpenXInput fork the first module named xinput1_4.dll
    /// in the process, before SDL_Init.
    ///
    /// <para>SDL's XInput driver loads the DLL by name
    /// (LoadLibrary("XInput1_4.dll"), SDL_xinput.c). The loader answers a
    /// load by name with a module of that name that is already loaded, the
    /// first one loaded when there are several. App startup only points the
    /// search path at the extraction folder (SetDllDirectory), so the fork
    /// wins only while nothing else has loaded Microsoft's copy. GameInput
    /// does: GameInputRedist.dll (the GameInput redistributable, version 3)
    /// loads System32's XInput1_4.dll itself, and SDL_Init brings GameInput
    /// up before the XInput driver asks for its DLL. On a PC with that
    /// redistributable, SDL then reads Microsoft's XInput, which gives
    /// PadForge's own HIDMaestro virtual controllers a slot. PadForge reads
    /// its virtual pad back as an input, and SDL's slot numbers no longer
    /// match OpenXInput's, which PadForge's own calls into the fork use.</para>
    ///
    /// <para>Loading the fork through the runtime's native library probing
    /// resolves it exactly as PadForge's [DllImport("xinput1_4.dll")] calls
    /// already do (the extracted copy beside SDL3.dll), and it happens on the
    /// input thread right before SDL_Init, the same moment SDL itself would
    /// have loaded it on a PC without GameInput.</para>
    /// </summary>
    public static class OpenXInputLoader
    {
        private const string ForkExport = "OpenXInputGetDeviceIdentityV1";

        /// <summary>True once the fork is the module SDL will get for
        /// "XInput1_4.dll". Read by the XInput identity lookup, which is only
        /// right when SDL and PadForge number slots the same way.</summary>
        public static bool ForkIsActive { get; private set; }

        /// <summary>Loads the fork first if nothing named xinput1_4.dll is
        /// loaded yet, and reports which copy a load by name now returns.
        /// Never throws.</summary>
        public static string EnsureForkFirst()
        {
            try
            {
                IntPtr existing = GetModuleHandleW("xinput1_4.dll");
                if (existing == IntPtr.Zero)
                {
                    if (!NativeLibrary.TryLoad("xinput1_4.dll", typeof(OpenXInputLoader).Assembly, null, out _))
                        return Report(IntPtr.Zero, "the OpenXInput fork could not be loaded");
                    existing = GetModuleHandleW("xinput1_4.dll");
                }
                return Report(existing, null);
            }
            catch (Exception ex)
            {
                ForkIsActive = false;
                return "OpenXInput: check failed: " + ex.Message;
            }
        }

        private static string Report(IntPtr module, string failure)
        {
            ForkIsActive = module != IntPtr.Zero && GetProcAddress(module, ForkExport) != IntPtr.Zero;
            string path = module == IntPtr.Zero ? "(none)" : ModulePath(module);
            if (ForkIsActive)
                return "OpenXInput: SDL will use the fork at " + path;
            return "OpenXInput: SDL will NOT use the fork (" + (failure ?? "Microsoft's copy was loaded first")
                + "), it gets " + path + ". PadForge's own virtual controllers can be read back as input.";
        }

        private static string ModulePath(IntPtr module)
        {
            var buffer = new char[1024];
            uint n = GetModuleFileNameW(module, buffer, (uint)buffer.Length);
            return n == 0 ? "(unknown)" : new string(buffer, 0, (int)n);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr GetModuleHandleW(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetModuleFileNameW(IntPtr hModule, char[] lpFilename, uint nSize);
    }
}
