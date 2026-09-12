//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite Display Controller (NemaDC, 1-layer @ 0x400A0000, IRQ29).
// NEW file for the Lite family — AmbiqApollo510_DC is NOT modified.
//
// NemaDC INTERRUPT protocol (third_party/ThinkSi/config/apollo510L_nemagfx/):
//   bit3 = TE (panel refresh), bits[5:4] = frame-done/vsync.
//   am_disp_isr: [5:4] -> vsync path (RMW-clear, frame_end, sem_vsync),
//     bit3 -> TE path (RMW-clear, launch-if-needed, te_cb/sem_TE).
//   The register is RW-stored status, cleared by RMW and by write(0).
//   Unlike the 510 model, bit3 is NOT masked on read.
//
// Free-running display timing: once the driver touches the DC (clocked +
// configured), a real NemaDC free-runs — vsync every refresh even with no
// frame in flight. Firmware RELIES on this: panel init ends with
// nemadc_wait_vsync with no frame launched (raydium init), and TCB forensics
// shows DisplayTask parked in xQueueSemaphoreTake with all tasks blocked.
// The model therefore runs a 60Hz virtual-time timer from the first DC
// register access after Reset, asserting vsync bits[5:4] + IRQ29 per refresh.
// TE (bit3) additionally requires arming: a write to INTERRUPT with bit3 set
// (the driver's "turn DC TE interrupt" kick). Before any DC access, nothing
// fires, so non-graphics firmware is unaffected (NVIC IRQ29 stays disabled
// there anyway).
//
// Frame completion: a MODE write with ONE_FRAME set completes the frame
// immediately (bits[5:4] + IRQ); the layer is copied to the viewer
// window @0x50000000 when L0BASE is programmed.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Timers;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Video
{
    public class AmbiqApollo510L_DC : BasicDoubleWordPeripheral, IKnownSize
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

        private const uint TEBit = 1u << 3;
        private const uint FrameDoneBits = 3u << 4;
        private const uint OneFrameBit = 1u << 17; // NEMADC_ONE_FRAME (nema_dc.h)
        // Panel refresh: 60 Hz (16.67 ms period) for vsync free-run and TE.
        private const ulong RefreshFrequencyHz = 60;

        private uint statusReg = 0;
        private uint interruptReg = 0;
        private uint modeReg = 0;
        private uint interfaceCfgReg = 0;
        private uint layer0Base = 0;
        private bool teArmed = false;
        private readonly LimitTimer refreshTimer;
        private readonly IMachine machine;

        public AmbiqApollo510L_DC(IMachine machine) : base(machine)
        {
            this.machine = machine;
            refreshTimer = new LimitTimer(machine.ClockSource, RefreshFrequencyHz, this, "refresh",
                limit: 1, direction: Direction.Ascending, enabled: false,
                workMode: WorkMode.Periodic, eventEnabled: true, autoUpdate: true);
            refreshTimer.LimitReached += OnRefresh;
        }

        public override void Reset()
        {
            refreshTimer.Enabled = false;
            teArmed = false;
            statusReg = 0;
            interruptReg = 0;
            modeReg = 0;
            interfaceCfgReg = 0;
            layer0Base = 0;
            IRQ.Unset();
        }

        public long Size => 0x2000; // covers 0x1C00 gamma LUT

        public override uint ReadDoubleWord(long offset)
        {
            switch(offset)
            {
                case REG_STATUS:    return statusReg; // wait_dbi_idle polls STATUS & mask == 0 -> 0 = idle passes
                case REG_INTERRUPT: return interruptReg; // unmasked: TE bit3 visible (unlike 510 model)
                case REG_MODE:      return modeReg;
                case REG_INTERFACE_CFG: return interfaceCfgReg;
                case REG_LAYER0_BASEADDR: return layer0Base;
                case 0xF4: // NEMADC_REG_IDREG: nemadc_init() (prebuilt lib) reads IDREG
                            // and returns FAILURE unless it equals 0x87452365 (see
                            // disassembly). Without this, display init aborts.
                    return 0x87452365;
                default:
                    this.Log(LogLevel.Noisy, "DC RD 0x{0:X}", offset);
                    if(offset < 0x200) return 0; // accept all layer/mode regs
                    return 0;
            }
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            // First touch after Reset means the driver is bringing the display
            // up: start free-running refresh (see class notes).
            if(!refreshTimer.Enabled)
            {
                refreshTimer.Enabled = true;
                this.Log(LogLevel.Info, "DC: display up, free-run vsync 60 Hz");
            }
            switch(offset)
            {
                case REG_INTERRUPT:
                    interruptReg = value;
                    if((value & TEBit) != 0 && !teArmed)
                    {
                        // Driver's "turn DC TE interrupt" kick: panel TE joins the refresh.
                        teArmed = true;
                        this.Log(LogLevel.Info, "DC: panel TE armed");
                    }
                    UpdateIRQ();
                    this.Log(LogLevel.Noisy, "DC WR INTERRUPT <- 0x{0:X}", value);
                    break;
                case REG_MODE:
                    modeReg = value;
                    this.Log(LogLevel.Info, "DC WR MODE <- 0x{0:X} {1}", value, (value & OneFrameBit) !=0 ? "ONE_FRAME" : "NORMAL");
                    if((value & OneFrameBit) != 0) // NEMADC_ONE_FRAME is bit17, not bit0
                    {
                        statusReg = 0;
                        CompleteFrame();
                    }
                    else
                    {
                        statusReg = 0;
                    }
                    break;
                case REG_INTERFACE_CFG:
                    interfaceCfgReg = value;
                    this.Log(LogLevel.Info, "DC WR IFCFG <- 0x{0:X}", value);
                    break;
                case REG_LAYER0_BASEADDR:
                    layer0Base = value;
                    this.Log(LogLevel.Info, "DC WR L0 BASE <- 0x{0:X} (FB phys from g_sFrameBuffer.bo.base_phys)", value);
                    break;
                default:
                    this.Log(LogLevel.Noisy, "DC WR 0x{0:X} <- 0x{1:X}", offset, value);
                    break;
            }
        }

        private void OnRefresh()
        {
            // Display refresh tick: vsync always (free-run), TE only if armed.
            // The ISR clears handled bits by RMW; the line follows status.
            interruptReg |= FrameDoneBits;
            if(teArmed)
            {
                interruptReg |= TEBit;
            }
            UpdateIRQ();
            this.Log(LogLevel.Noisy, "DC refresh tick IRQ29");
        }

        private void CompleteFrame()
        {
            interruptReg |= FrameDoneBits;
            UpdateIRQ();
            this.Log(LogLevel.Info, "DC: frame done L0 0x{0:X} IRQ29", layer0Base);
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

        private void UpdateIRQ()
        {
            if(interruptReg != 0)
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
