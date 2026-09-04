// AmbiqApollo510 Display Controller (NemaDC) peripheral - companion to GPU
// LOCATION: /tmp/AmbiqApollo510_DC.cs
// COPY TO: renode-source/src/Infrastructure/src/Emulator/Peripherals/Video/AmbiqApollo510_DC.cs
// REPL:   dc: Video.AmbiqApollo510_DC @ sysbus 0x400A0000
//           -> nvic@29
//         display: Video.PixelDisplay @ sysbus 0x03800000  # or PSRAM 0x14000000 + FB phys from Layer0 BASEADDR
//                 width: 390 height: 390  # nemagfx_watchface FB_RESX 390
//                 format: PixelFormat.RGB565
//
// Based on: ThinkSi/NemaDC/nema_dc_regs.h  STATUS 0xFC, INTERRUPT 0xF8, MODE 0x00
//           apollo510.h:29542 DC_BASE 0x400A0000, NEMADC_IRQ 29 per nema_dc_hal.c:86

using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Video
{
    public class AmbiqApollo510_DC : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();
        private const long REG_MODE            = 0x00;
        private const long REG_CLKCTRL         = 0x04;
        private const long REG_BGCOLOR         = 0x08;
        private const long REG_RESXY           = 0x0C;
        private const long REG_INTERRUPT       = 0xF8;
        private const long REG_STATUS          = 0xFC;
        private const long REG_CLKCTRL_CG      = 0x1A8;
        private const long REG_INTERFACE_CFG   = 0x28; // DBIB_CFG
        private const long REG_LAYER0_BASEADDR = 0x3C;
        private const long REG_LAYER0_STRIDE   = 0x40;
        private const long REG_LAYER0_RESXY    = 0x44;

        private uint statusReg = 0;
        private uint interruptReg = 0;
        private uint layer0Base = 0;
        private readonly IMachine machine;
        public AmbiqApollo510_DC(IMachine machine) : base(machine) { this.machine = machine; }

        public override void Reset()
        {
            statusReg = 0;
            interruptReg = 0;
            layer0Base = 0;
        }

        public long Size => 0x2000; // covers 0x1C00 gamma LUT

        public override uint ReadDoubleWord(long offset)
        {
            switch(offset)
            {
                case REG_STATUS:    return statusReg; // wait_dbi_idle:244 polls DC->STATUS & mask ==0 -> return 0 = idle passes
                case REG_INTERRUPT:
                    // Long-term: vsync wait checks bit3==0, return 0 for bit3 so wait succeeds immediately
                    // Keep bit4 for IRQ handling, but mask bit3
                    return interruptReg & ~(1u << 3);
                case REG_LAYER0_BASEADDR: return layer0Base;
                case 0x1F4: // NEMADC_REG_IDREG equivalent: nemadc_init() reads IP_VERSION @0xF4
                            // and compares against the NemaDC IP signature 0x87452365.
                    this.Log(LogLevel.Info, "DC RD IP_VERSION -> 0x87452365");
                    return 0x87452365;
                default:
                    if(offset < 0x200) return 0; // accept all layer/mode regs
                    this.Log(LogLevel.Noisy, "DC RD 0x{0:X}", offset);
                    return 0;
            }
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            switch(offset)
            {
                case REG_INTERRUPT:
                    if(value == 0) { interruptReg = 0; IRQ.Unset(); }
                    else { interruptReg = value; if(value != 0) IRQ.Set(); }
                    this.Log(LogLevel.Noisy, "DC WR INTERRUPT <- 0x{0:X}", value);
                    break;
                case REG_MODE:
                    this.Log(LogLevel.Info, "DC WR MODE <- 0x{0:X} {1}", value, (value & 1) !=0 ? "ONE_FRAME" : "NORMAL");
                    if((value & 1) != 0)
                    {
                        statusReg = 0;
                        // Set both TE (bit3) and VSYNC (bit4) so am_hal_delay_us_status_change for bit3 succeeds
                        // The firmware's nemadc_wait_vsync waits for INTERRUPT bit 3 (TE) to be 0, but real HW sets it on frame done
                        // In simulation, we want the wait to succeed immediately, so we ensure bit3 is 0 and bit4 is set for IRQ
                        interruptReg = (1 << 3) | (1 << 4);
                        IRQ.Set();
                        this.Log(LogLevel.Info, "DC: frame done L0 0x{0:X} IRQ29 TE+VSYNC", layer0Base);
                        // Immediately clear TE bit so wait succeeds (wait checks for 0)
                        // Keep VSYNC for IRQ handling
                        interruptReg = 1 << 4;
                        if(layer0Base != 0)
                        {
                            try
                            {
                                var sysbus = machine.GetSystemBus(this);
                                var data = sysbus.ReadBytes((ulong)layer0Base, 0x80000);
                                sysbus.WriteBytes(data, 0x50000000);
                                this.Log(LogLevel.Info, "DC: FB copied 512K to viewer @0x50000000");
                            } catch(Exception ex) { this.Log(LogLevel.Warning, "DC viewer copy failed: {0}", ex.Message); }
                        }
                    }
                    else
                    {
                        statusReg = 0;
                        interruptReg = 0;
                        IRQ.Unset();
                    }
                    break;
                case REG_LAYER0_BASEADDR:
                    layer0Base = value;
                    this.Log(LogLevel.Info, "DC WR L0 BASE <- 0x{0:X} (FB phys from g_sFrameBuffer.bo.base_phys)", value);
                    // Hook for PixelDisplay: if you have apollo510b.repl display @ FB phys, this is where analyzer would capture
                    break;
                default:
                    if(offset < 0x200) break; // accept
                    this.Log(LogLevel.Noisy, "DC WR 0x{0:X} <- 0x{1:X}", offset, value);
                    break;
            }
        }
    }
}
