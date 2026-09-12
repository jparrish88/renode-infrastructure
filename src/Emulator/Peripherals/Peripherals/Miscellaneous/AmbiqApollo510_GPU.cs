// AmbiqApollo510 GPU (NemaGFX) peripheral - complete enough for nemagfx_watchface + lvgl_watch_face
// LOCATION: /tmp/AmbiqApollo510_GPU.cs  (tmp only, per your request - not in renode-source/)
// COPY TO:  renode-source/src/Infrastructure/src/Emulator/Peripherals/GPU/AmbiqApollo510_GPU.cs when ready to integrate
// REPL:     gpu: GPU.AmbiqApollo510_GPU @ sysbus 0x40090000
//             -> nvic@28
//           ssram: Memory.MappedMemory @ sysbus 0x03800000  size: 0x80000   # tsi_buffer VMEM_SIZE 0x180000 but 512K covers it, see nema_hal.c:71
//
// Based on: AmbiqSuite_5.1.0/third_party/ThinkSi/config/apollo510_nemagfx/nema_hal.c:91 NEMA_IRQ 28
//           AmbiqSuite_5.1.0/third_party/ThinkSi/NemaGFX_SDK/NemaGFX/Nema/nema_regs.h  STATUS 0x0FC, CLID 0x148, INTERRUPT 0x0F8
//           apollo510.h:29546 GPU_BASE 0x40090000UL
//
// What it does:
//  - Emulates NEMA_STATUS/CLID/INTERRUPT so nema_hal.c:568,753,149,267 and render_task.c:471 never hang.
//  - Ringbuffer submit at CMDADDR/CMDSIZE triggers async completion -> IRQ28 -> prvNemaInterruptHandler:139
//  - No RTL needed - app draws are no-ops, FrameBuffer stays as CPU wrote it, but tasks proceed.
//  - Functional path (optional): if you wire framebuffer phys to PixelDisplay, you get black->hand draws skipped, but can extend.

using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.CPU;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510_GPU : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();
        // Register map from nema_regs.h
        private const long REG_STATUS          = 0x0FC; // NEMA_STATUS - 0 idle, !=0 busy
        private const long REG_CLID            = 0x148; // NEMA_CLID - last completed CL id
        private const long REG_INTERRUPT       = 0x0F8; // NEMA_INTERRUPT - write 0 to clear
        private const long REG_CMDADDR         = 0x0F0; // NEMA_CMDADDR (+0x84C high)
        private const long REG_CMDSIZE         = 0x0F4; // NEMA_CMDSIZE
        private const long REG_CMDSTATUS       = 0x0E8;
        private const long REG_CMDRINGSTOP     = 0x0EC;
        private const long REG_BURST_SIZE      = 0x0D0;
        private const long REG_BREAKPOINT      = 0x080;
        private const long REG_BREAKPOINT_MASK = 0x08C;
        private const long REG_CONFIG          = 0x1F0;
        private const long REG_IDREG           = 0x1EC;

        private uint statusReg = 0;      // start idle
        private int lastClId = -1;       // visible as CLID, matches nema_hal.c:135
        private int nextClId = 0;
        private uint interruptReg = 0;
        private uint cmdAddrLow = 0;
        private uint cmdSize = 0;

        public AmbiqApollo510_GPU(IMachine machine) : base(machine)
        {
            Reset();
        }

        public override void Reset()
        {
            statusReg = 0;
            lastClId = -1;
            nextClId = 0;
            interruptReg = 0;
            cmdAddrLow = 0;
            cmdSize = 0;
        }

        public long Size => 0x1000;

        private void RaiseIRQ()
        {
            interruptReg = 1;
            lastClId = nextClId++;
            statusReg = 0;
            this.Log(LogLevel.Info, "GPU: CL {0} submitted -> CLID {1} IRQ", nextClId-1, lastClId);
            IRQ.Set();
        }

        public override uint ReadDoubleWord(long offset)
        {
            switch(offset)
            {
                case REG_STATUS:      return statusReg;
                case REG_CLID:        this.Log(LogLevel.Noisy, "GPU RD CLID -> {0}", lastClId); return (uint)lastClId;
                case REG_INTERRUPT:   this.Log(LogLevel.Noisy, "GPU RD INTERRUPT -> {0}", interruptReg); return interruptReg;
                case REG_CMDADDR:     return cmdAddrLow;
                case REG_CMDSIZE:     return cmdSize;
                case REG_IDREG:       return 0x4E454D41; // "NEMA" placeholder
                case REG_CONFIG:      return 0;
                case REG_BREAKPOINT:
                case REG_BREAKPOINT_MASK: return 0;
                case REG_CMDSTATUS:   return 0;
                case REG_BURST_SIZE:  return 4;
                default:
                    if(offset >= 0x000 && offset < 0x400) // texture/raster regs - just storage
                    {
                        return 0;
                    }
                    this.Log(LogLevel.Noisy, "GPU RD unhandled 0x{0:X}", offset);
                    return 0;
            }
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            switch(offset)
            {
                case REG_INTERRUPT:
                    this.Log(LogLevel.Noisy, "GPU WR INTERRUPT <- 0x{0:X}", value);
                    if(value == 0) { interruptReg = 0; IRQ.Unset(); }
                    else interruptReg = value;
                    break;

                case REG_CMDADDR:
                    cmdAddrLow = value;
                    this.Log(LogLevel.Info, "GPU WR CMDADDR <- 0x{0:X} (CL submit trigger)", value);
                    // Nema ringbuffer submits by writing CMDADDR then CMDSIZE - treat CMDADDR as kick
                    if(value != 0) RaiseIRQ();
                    break;

                case REG_CMDSIZE:
                    cmdSize = value;
                    this.Log(LogLevel.Info, "GPU WR CMDSIZE <- 0x{0:X}", value);
                    // Some SDK versions trigger on CMDSIZE write - also kick
                    if(cmdSize != 0 && cmdAddrLow != 0) RaiseIRQ();
                    break;

                case REG_CMDRINGSTOP:
                    // Ringbuffer doorbell: nema_rb_force_flush() writes the updated
                    // stop pointer here after queueing a CL (CLID + INTERRUPT inline
                    // cmds live in the SRAM ring, so this write is the only MMIO
                    // sign of a frame submit). Complete one CL like RaiseIRQ().
                    this.Log(LogLevel.Info, "GPU WR CMDRINGSTOP <- 0x{0:X} (ringbuffer submit, complete CL)", value);
                    RaiseIRQ();
                    break;
                case REG_CONFIG:
                case REG_BURST_SIZE:
                case REG_BREAKPOINT:
                case REG_BREAKPOINT_MASK:
                    this.Log(LogLevel.Noisy, "GPU WR 0x{0:X} <- 0x{1:X}", offset, value);
                    break;

                default:
                    if(offset >= 0x000 && offset < 0x400)
                    {
                        // Silently accept rasterizer/tex regs - render_task.c:261 blit etc writes these
                        // Functional renderer would capture them here to software rasterize
                        break;
                    }
                    this.Log(LogLevel.Noisy, "GPU WR 0x{0:X} <- 0x{1:X}", offset, value);
                    break;
            }
        }
    }
}
