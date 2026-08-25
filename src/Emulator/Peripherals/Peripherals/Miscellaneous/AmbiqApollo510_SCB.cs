//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Minimal ARM SCB (System Control Block) model for the Apollo510B. The generic CPU.CortexM does not
    // expose its SCB registers as memory-mapped IO, so this peripheral absorbs accesses in the 0xE000EDxx
    // window and forwards the fault-status registers to the connected CPU's tlib state (CFSR/MMFAR/BFAR).
    // This makes a HardFault's class + address observable headlessly: AMBiq's am_util_faultisr_collect_data
    // reads CFSR after a fault, which is logged here with a decoded breakdown. It also honours ACTLR/CPACR
    // writes to enable the FPU (cpu.FpuEnabled) so hard-float firmware can proceed past FP init without trapping.
    public class AmbiqApollo510_SCB : IDoubleWordPeripheral, IKnownSize
    {
        private readonly IMachine machine;
        private CortexM cpu;

        // Latched read/write register values (VTOR, CCR, CPACR, SHPR0, SCR, ...). The generic CortexM does not
        // expose these as MMIO, so firmware's read-backs must see what it wrote. Critically, FreeRTOS'
        // vStartFirstTask reads SCB->VTOR then dereferences [base][0] to load the first task's initial SP;
        // if VTOR reads return 0 (unlatched) the scheduler starts with a corrupt MSP -> wild memory accesses.
        private readonly Dictionary<long, uint> state = new();

        // Offsets below are relative to the peripheral base (placed at 0xE000ED00).
        private const long CpuID = 0x00;     // ISBRO..PMR
        private const long Icsr = 0x04;
        private const long Vtor = 0x08;
        private const long Aircr = 0x0C;
        private const long Scr = 0x10;
        private const long Ccr = 0x14;
        private const long Shcsr = 0x24;
        private const long Cfsr = 0x28;      // Configurable Fault Status Register
        private const long HfSr = 0x2C;      // HardFault Status Register
        private const long Dfsr = 0x30;      // Debug Fault Status Register
        private const long MmfAr = 0x34;     // MemManage Fault Address Register
        private const long BfAr = 0x38;      // BusFault Address Register
        private const long Cpacr = 0x88;     // Coprocessor Access Control (FP enable)
        private const long Actlr = 0x8C;     // Auxiliary Control (FPCA, bit24)

        public AmbiqApollo510_SCB(IMachine machine)
        {
            this.machine = machine;

            // Default vector-table base for this platform (matches the VectorTableOffset / firmware's own write).
            state[Vtor] = 0x410000u;
        }

        public void Reset() { }

        // Covers CPUID(0x0)..ACTLR(0x8C); stops before DCB@0xE000EDF0. NVIC owns the lower 0xE000E0xx region.
        public long Size => 0xF0;

        private CortexM GetCPU()
        {
            if (cpu == null)
            {
                cpu = machine.SystemBus.GetCPUs().OfType<CortexM>().FirstOrDefault();
            }

            return cpu;
        }

        public uint ReadDoubleWord(long offset)
        {
            LogRead(offset);
            var c = GetCPU();

            switch (offset)
            {
                case Cfsr:
                    if (c == null)
                    {
                        return 0;
                    }

                    var cfsr = c.FaultStatus;
                    this.Log(LogLevel.Info, "[SCB] read CFSR=0x{0:X8} -> {1}", cfsr, DecodeCfsr(cfsr));
                    return cfsr;

                case HfSr:
                    // No direct tlib accessor for HFSR; report FORCED (bit0) when a fault is latched.
                    var hfsr = (c != null && c.FaultStatus != 0u) ? 1u : 0u;
                    this.Log(LogLevel.Info, "[SCB] read HFSR=0x{0:X8}", hfsr);
                    return hfsr;

                case MmfAr:
                    if (c == null)
                    {
                        return 0;
                    }

                    var mmfar = c.MemoryFaultAddress;
                    this.Log(LogLevel.Info, "[SCB] read MMFAR=0x{0:X8}", mmfar);
                    return mmfar;

                case BfAr:
                    if (c == null)
                    {
                        return 0;
                    }

                    var bfar = c.BusFaultAddress;
                    this.Log(LogLevel.Info, "[SCB] read BFAR=0x{0:X8}", bfar);
                    return bfar;

                case Ccr:
                    if (c == null)
                    {
                        return state.TryGetValue(offset, out var ccVal) ? ccVal : 0u;
                    }

                    return c.ConfigurationAndControlRegister;

                case Vtor:
                    var vtVal = state.TryGetValue(offset, out var vtRaw) ? vtRaw : 0u;
                    this.Log(LogLevel.Info, "[SCB] read VTOR=0x{0:X8}", vtVal);
                    return vtVal;

                default:
                    // CPUID / ICSR / SCR / SHPR0 etc. -> latched write value if any (read-back fidelity), else zero.
                    return state.TryGetValue(offset, out var rwVal) ? rwVal : 0u;
            }
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            LogWrite(offset, value);
            var c = GetCPU();
            if (c == null)
            {
                return;
            }

            switch (offset)
            {
                case Cpacr:
                    // FP access is enabled when TEN(bit2) and TSP(bit3) are cleared.
                    if ((value & 0x18u) == 0u)
                    {
                        c.FpuEnabled = true;
                        this.Log(LogLevel.Info, "[SCB] write CPACR=0x{0:X8}: FPU enabled", value);
                    }

                    break;

                case Actlr:
                    // ACTLR bit24 (FPCA) requests FP context preservation/access.
                    if ((value & 0x1000000u) != 0u)
                    {
                        c.FpuEnabled = true;
                        this.Log(LogLevel.Info, "[SCB] write ACTLR=0x{0:X8}: FPU enabled", value);
                    }

                    break;

                case Aircr:
                    if ((value & 0x5FAu) == 0x5FAu)
                    {
                        this.Log(LogLevel.Info, "[SCB] write AIRCR=0x{0:X8}: system reset requested", value);
                    }

                    break;

                case Vtor:
                    // Vector-table base. Latched so vStartFirstTask's read of [SCB_VTOR][0] yields the real initial SP.
                    this.Log(LogLevel.Info, "[SCB] write VTOR=0x{0:X8} (vector-table base latched)", value);

                    break;
            }

            // Latch every RW write so later reads return what firmware wrote (VTOR/CCR/CPACR/SHPR0/SCR).
            state[offset] = value;
        }

        private readonly HashSet<long> seenRead = new();
        private int writeCount;

        private void LogRead(long offset)
        {
            if (seenRead.Add(offset))
            {
                this.Log(LogLevel.Info, "[SCB] read off=0x{0:X}", offset);
            }
        }

        private void LogWrite(long offset, uint value)
        {
            if (writeCount < 60)
            {
                this.Log(LogLevel.Info, "[SCB] write off=0x{0:X} val=0x{1:X8}", offset, value);
            }

            writeCount++;
        }

        private static string DecodeCfsr(uint cfsr)
        {
            if (cfsr == 0u)
            {
                return "no fault bits";
            }

            var parts = new List<string>();
            if ((cfsr & 0x1FFFu) != 0u)
            {
                parts.Add(string.Format("UsageFault=0x{0:X4}", cfsr & 0x1FFFu));
            }

            if ((cfsr & 0xFFFC0000u) != 0u)
            {
                parts.Add(string.Format("MemManage bits[25:16]=0x{0:X2} MMARVALID={1}", (cfsr >> 16) & 0xFFu, (cfsr & (1u << 7)) != 0u));
            }

            if ((cfsr & 0x003F0000u) != 0u)
            {
                parts.Add(string.Format("BusFault bits[31:26]=0x{0:X2} BFARVALID={1}", (cfsr >> 26) & 0x3Fu, (cfsr & (1u << 1)) != 0u));
            }

            return string.Join(", ", parts);
        }
    }
}
