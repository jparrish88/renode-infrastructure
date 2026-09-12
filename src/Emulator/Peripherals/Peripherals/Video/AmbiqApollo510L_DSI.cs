//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite MIPI DSI controller (@ 0x400A8000, IRQ30).
// NEW file for the Lite family.
//
// Only the init/status path is modeled; DCS traffic itself flows through
// the DC's DBIB engine (nemadc_mipi_cmd_write reads NEMADC_REG_DBIB_CFG),
// not these registers. Register map from pack/SVD/apollo510L.svd.
//
// Faithful behaviors:
//   DEVICEREADY.READY[0]: RW flag. The HAL sets it during am_hal_dsi_init.
//   INTRSTAT.INITDONE[28]: mirror of READY. am_hal_dsi_init polls
//     INITDONE==1 with a 1ms timeout and returns TIMEOUT otherwise — with a
//     silent all-zero stub, display init dies here. (Same shared-flag lesson
//     as PWRCTRL/CRM.)
//   INTRSTAT.LOWC[19] and all other status bits: stored, write-1-to-clear
//     (the HAL does `INTRSTAT_b.LOWC = 1` to clear). Rest read 0: no errors,
//     TX FIFO reports empty.
//   INTRSTAT.FIFOEMPTY[20]: constant 1 (TX FIFO drains instantly in sim).
//   IRQ30 asserts while (INTRSTAT & INTREN) != 0 (generic rule; INTREN
//   resets 0 so nothing fires until firmware opts in).
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Video
{
    public class AmbiqApollo510L_DSI : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_DSI(IMachine machine) : base(machine)
        {
        }

        private const long REG_DEVICEREADY = 0x00;
        private const long REG_INTRSTAT    = 0x04;
        private const long REG_INTREN      = 0x08;

        private const uint INITDONEBit = 1u << 28;
        private const uint FIFOEMPTYBit = 1u << 20;

        private uint deviceReady; // full RW (READY[0], ULPS[2:1], DISPLAYBUSPOSSESSEN[3])
        private uint intrStat; // W1C status bits (INITDONE/FIFOEMPTY excluded: derived)
        private uint intrEn;

        public override void Reset()
        {
            deviceReady = 0;
            intrStat = 0;
            intrEn = 0;
            IRQ.Unset();
        }

        public long Size => 0x100;

        public override uint ReadDoubleWord(long offset)
        {
            switch(offset)
            {
                case REG_DEVICEREADY: return deviceReady;
                case REG_INTRSTAT: return intrStat | FIFOEMPTYBit | ((deviceReady & 1u) != 0 ? INITDONEBit : 0u);
                case REG_INTREN: return intrEn;
                default:
                    if(offset < 0x100) return 0;
                    this.Log(LogLevel.Noisy, "DSI RD 0x{0:X}", offset);
                    return 0;
            }
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            switch(offset)
            {
                case REG_DEVICEREADY:
                    deviceReady = value;
                    this.Log(LogLevel.Info, "DSI DEVICEREADY <- 0x{0:X} (READY={1})", value, (value & 1u) != 0);
                    UpdateIRQ();
                    break;
                case REG_INTRSTAT:
                    // Write-1-to-clear.
                    intrStat &= ~value;
                    UpdateIRQ();
                    break;
                case REG_INTREN:
                    intrEn = value;
                    UpdateIRQ();
                    break;
                default:
                    if(offset < 0x100) break; // accept (panel/trim regs RAZ/WI for now)
                    this.Log(LogLevel.Noisy, "DSI WR 0x{0:X} <- 0x{1:X}", offset, value);
                    break;
            }
        }

        private void UpdateIRQ()
        {
            var pending = (ReadDoubleWord(REG_INTRSTAT) & intrEn) != 0;
            if(pending)
            {
                IRQ.Set();
            }
            else
            {
                IRQ.Unset();
            }
        }
    }
}
