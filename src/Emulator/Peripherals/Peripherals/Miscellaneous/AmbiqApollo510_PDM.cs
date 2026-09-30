//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 PDM0 (PDM @ 0x40201000, IRQ48).
// NEW file for the Apollo510 family.
//
// Register map + reset values from SVD/apollo510.svd (21 regs).
// Differs from the Lite PDM only in DMATOTCOUNT (0x250 here, 0x150 there).
// Phase-2 model: paced DMA completion for power profiling.
//  - On DMA arm (DMACFG.DMAEN + DMATRIGEN.DTHR with CTRL enabled) a
//    virtual-time LimitTimer fires once per audio buffer (TOTCOUNT bytes at
//    the PDM sample rate: bytes/4 samples @16kHz -> 64ms for 4096B).
//  - On expiry the target buffer is zero-filled via sysbus (FFT cost is
//    data-independent; a synthetic tone can replace zeros if SWO peak
//    validation is ever needed), DMASTAT.DMACPL + INTSTAT.DCMP are set,
//    and IRQ48 follows INTSTAT & INTEN.
//  - INTCLR is write-1-to-clear, INTSET sets INTSTAT bits.
// Pacing is deliberate: instant completion would collapse the firmware's
// sleep-between-buffers duty cycle and invalidate average power.
//
using System;
using System.Collections.Generic;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Timers;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class AmbiqApollo510_PDM : BasicDoubleWordPeripheral, IKnownSize
    {
        // PDM sample rate used for pacing (16kHz for the SDK pdm_fft config:
        // PLL 24.576MHz / 2 / 6 / 2 / 64). Firmware with a different clock
        // gets a proportionally wrong period; TOTCOUNT scaling still applies.
        private const double SampleRateHz = 16000.0;

        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510_PDM(IMachine machine) : base(machine)
        {
            this.machine = machine;
            Reset();
        }

        public long Size => 0x300;

        public override void Reset()
        {
            registers.Clear();
            foreach(var kv in Resets)
            {
                registers[kv.Key] = kv.Value;
            }
            StopDmaTimer();
            UpdateIRQ();
        }

        public override uint ReadDoubleWord(long offset)
        {
            if(registers.TryGetValue(offset, out var value))
            {
                return value;
            }
            return 0;
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            if(offset == 0x108) // INTCLR: write-1-to-clear
            {
                registers[0x104] = registers.TryGetValue(0x104, out var st) ? (uint)(st & ~value) : 0u;
                registers[offset] = 0;
                UpdateIRQ();
                return;
            }
            if(offset == 0x10C) // INTSET: set INTSTAT bits
            {
                registers[0x104] = registers.TryGetValue(0x104, out var st2) ? (uint)(st2 | value) : value;
                registers[offset] = value;
                UpdateIRQ();
                return;
            }
            registers[offset] = value;
            if(offset == 0x14) // FIFOREAD is read-only in HW, but log writes
            {
                this.Log(LogLevel.Info, "PDM WR FIFOREAD <- 0x{0:X} (ignored)", value);
            }
            else if(offset == 0x18 && (value & 0x1u) != 0) // FIFOFLUSH
            {
                registers[0x10] = 0; // FIFOCNT
            }
            else if(offset == 0x148 || offset == 0x140 || offset == 0x00)
            {
                TryArmDma();
                if(offset == 0x148 && (value & 0x1u) == 0)
                {
                    StopDmaTimer(); // DMAEN cleared: stop pacing
                }
            }
            if(IsIrqRegister(offset))
            {
                UpdateIRQ();
            }
        }

        private void TryArmDma()
        {
            uint ctrl = registers.TryGetValue(0x00, out var c) ? c : 0u;
            uint cfg = registers.TryGetValue(0x148, out var g) ? g : 0u;
            uint trig = registers.TryGetValue(0x140, out var t) ? t : 0u;
            if(ctrl == 0 || (cfg & 0x1u) == 0 || (trig & 0x1u) == 0)
            {
                return;
            }
            if(dmaTimer != null && dmaTimer.Enabled)
            {
                return; // already pacing
            }
            uint tot = registers.TryGetValue(0x250, out var n) ? n : 0u;
            if(tot == 0) tot = 4096;
            double seconds = (tot / 4.0) / SampleRateHz;
            ulong freq = (ulong)Math.Max(Math.Round(1.0 / Math.Max(seconds, 1e-6)), 1);
            StopDmaTimer();
            dmaTimer = new LimitTimer(machine.ClockSource, freq, this, "pdm-dma",
                limit: 1, direction: Direction.Ascending, enabled: true,
                workMode: WorkMode.Periodic, eventEnabled: true, autoUpdate: true);
            dmaTimer.LimitReached += OnDmaBufferDone;
            this.Log(LogLevel.Info, "PDM DMA pacing armed: {0}B per {1:F1}ms", tot, seconds * 1000.0);
        }

        private void StopDmaTimer()
        {
            // LimitTimer is not disposable; abandon it disabled (one per
            // reset at most, re-armed only when firmware restarts DMA).
            if(dmaTimer != null)
            {
                dmaTimer.Enabled = false;
                dmaTimer = null;
            }
        }

        private void OnDmaBufferDone()
        {
            uint addr = registers.TryGetValue(0x154, out var a) ? a : 0u;
            uint tot = registers.TryGetValue(0x250, out var n) ? n : 0u;
            if(tot == 0) tot = 4096;
            if(addr != 0)
            {
                Sysbus.WriteBytes(new byte[tot], addr);
            }
            registers[0x158] = registers.TryGetValue(0x158, out var ds) ? ds | 0x2u : 0x2u; // DMACPL
            registers[0x104] = registers.TryGetValue(0x104, out var st) ? st | 0x8u : 0x8u; // DCMP
            registers[0x10] = 16; // FIFOCNT at threshold
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            uint stat = registers.TryGetValue(0x104, out var s) ? s : 0u;
            uint en = registers.TryGetValue(0x100, out var e) ? e : 0u;
            if((stat & en) != 0)
            {
                IRQ.Set();
            }
            else
            {
                IRQ.Unset();
            }
        }

        private static bool IsIrqRegister(long offset)
        {
            return offset == 0x100 || offset == 0x104 || offset == 0x108 || offset == 0x10C;
        }

        private IBusController Sysbus => machine.GetSystemBus(this);

        private readonly IMachine machine;
        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();
        private LimitTimer dmaTimer;

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000000), // CTRL
            new KeyValuePair<long, uint>(0x04, 0x42100364), // CORECFG0
            new KeyValuePair<long, uint>(0x08, 0x00B28105), // CORECFG1
            new KeyValuePair<long, uint>(0x0C, 0x80000434), // CORECTRL
            new KeyValuePair<long, uint>(0x10, 0x00000000), // FIFOCNT
            new KeyValuePair<long, uint>(0x14, 0x00000000), // FIFOREAD
            new KeyValuePair<long, uint>(0x18, 0x00000000), // FIFOFLUSH
            new KeyValuePair<long, uint>(0x1C, 0x00000010), // FIFOTHR
            new KeyValuePair<long, uint>(0x100, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x104, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x108, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x10C, 0x00000000), // INTSET
            new KeyValuePair<long, uint>(0x140, 0x00000000), // DMATRIGEN
            new KeyValuePair<long, uint>(0x144, 0x00000000), // DMATRIGSTAT
            new KeyValuePair<long, uint>(0x148, 0x00000000), // DMACFG
            new KeyValuePair<long, uint>(0x250, 0x00000000), // DMATOTCOUNT
            new KeyValuePair<long, uint>(0x154, 0x00000000), // DMATARGADDR
            new KeyValuePair<long, uint>(0x158, 0x00000000), // DMASTAT
            new KeyValuePair<long, uint>(0x160, 0x00000000), // DMATARGADDRNEXT
            new KeyValuePair<long, uint>(0x164, 0x00000000), // DMATOTCOUNTNEXT
            new KeyValuePair<long, uint>(0x168, 0x00000000), // DMAENNEXTCTRL
        };
    }
}
