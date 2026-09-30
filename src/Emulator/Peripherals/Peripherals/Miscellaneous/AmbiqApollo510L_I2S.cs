//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
 // Apollo510 Lite I2S0/I2S1 (I2S @ 0x40208000/0x40209000, IRQ44).
// NEW file for the Lite family (register-identical on Apollo510).
//
// Register map + reset values from pack/SVD/apollo510L.svd (36 regs).
// Phase-2 model: paced DMA completion with true loopback.
//  - On DMA arm (DMACFG.RXDMAEN/TXDMAEN with I2SCTL RXEN/TXEN) a
//    virtual-time LimitTimer fires once per audio buffer. TOTCNT is in
//    32-bit words; the pace is words / sampleRate, where sampleRate =
//    BCLK / framePeriod, framePeriod = I2SIOCFG.FPER+1 bit clocks, BCLK =
//    3.072MHz I2S bit-clock convention (48kHz x 64fs; matches the SDK
//    loopback clocking). Logged at arm time.
//  - On expiry: TX buffer is looped back into the RX buffer via sysbus
//    (physical TX->RX wiring on loopback EVBs), RXDMASTAT/TXDMASTAT DMACPL
//    + INTSTAT RXDMACPL/TXDMACPL are set, IRQ follows INTSTAT & INTEN.
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
    public class AmbiqApollo510L_I2S : BasicDoubleWordPeripheral, IKnownSize
    {
        // I2S bit-clock convention (48kHz stereo x 64 bit clocks/frame).
        private const double BitClockHz = 3072000.0;

        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo510L_I2S(IMachine machine) : base(machine)
        {
            this.machine = machine;
            Reset();
        }

        public long Size => 0x400;

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
            if(offset == 0x308) // INTCLR: write-1-to-clear
            {
                registers[0x304] = registers.TryGetValue(0x304, out var st) ? (uint)(st & ~value) : 0u;
                registers[offset] = 0;
                UpdateIRQ();
                return;
            }
            if(offset == 0x30C) // INTSET: set INTSTAT bits
            {
                registers[0x304] = registers.TryGetValue(0x304, out var st2) ? (uint)(st2 | value) : value;
                registers[offset] = value;
                UpdateIRQ();
                return;
            }
            registers[offset] = value;
            if(offset == 0x200 || offset == 0x48
               || offset == 0x204 || offset == 0x208
               || offset == 0x210 || offset == 0x214)
            {
                // DMACFG/I2SCTL arm the pacing; TOTCNT/ADDR writes cover
                // programs that touch count/address after the enable bits.
                TryArmDma();
                uint cfg = registers.TryGetValue(0x200, out var g) ? g : 0u;
                if((cfg & 0x11u) == 0)
                {
                    StopDmaTimer(); // both DMAENs cleared: stop pacing
                }
            }
            if(IsIrqRegister(offset))
            {
                UpdateIRQ();
            }
        }

        private void TryArmDma()
        {
            uint ctl = registers.TryGetValue(0x48, out var c) ? c : 0u;
            uint cfg = registers.TryGetValue(0x200, out var g) ? g : 0u;
            bool rxOn = (cfg & 0x01u) != 0 && (ctl & 0x10u) != 0;
            bool txOn = (cfg & 0x10u) != 0 && (ctl & 0x01u) != 0;
            if(!rxOn && !txOn)
            {
                return;
            }
            if(dmaTimer != null && dmaTimer.Enabled)
            {
                return; // already pacing
            }
            uint rxWords = registers.TryGetValue(0x204, out var r) ? r : 0u;
            uint txWords = registers.TryGetValue(0x210, out var t) ? t : 0u;
            uint words = rxOn ? rxWords : txWords;
            if(words == 0)
            {
                return; // addresses programmed before count: wait for TOTCNT
            }
            uint iocfg = registers.TryGetValue(0x44, out var io) ? io : 0u;
            uint fper = ((iocfg >> 4) & 0xFFFu) + 1;
            double sampleRate = BitClockHz / Math.Max(fper, 1);
            double seconds = words / sampleRate;
            ulong freq = (ulong)Math.Max(Math.Round(1.0 / Math.Max(seconds, 1e-6)), 1);
            StopDmaTimer();
            dmaTimer = new LimitTimer(machine.ClockSource, freq, this, "i2s-dma",
                limit: 1, direction: Direction.Ascending, enabled: true,
                workMode: WorkMode.Periodic, eventEnabled: true, autoUpdate: true);
            dmaTimer.LimitReached += OnDmaBufferDone;
            this.Log(LogLevel.Info, "I2S DMA pacing armed: {0} words at {1:F1}kHz ({2:F1}ms)",
                words, sampleRate / 1000.0, seconds * 1000.0);
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
            uint rxAddr = registers.TryGetValue(0x208, out var ra) ? ra : 0u;
            uint rxWords = registers.TryGetValue(0x204, out var rn) ? rn : 0u;
            uint txAddr = registers.TryGetValue(0x214, out var ta) ? ta : 0u;
            uint txWords = registers.TryGetValue(0x210, out var tn) ? tn : 0u;
            uint ctl = registers.TryGetValue(0x48, out var c) ? c : 0u;
            uint cfg = registers.TryGetValue(0x200, out var g) ? g : 0u;
            // True loopback: TX stream appears at RX (EVB TX->RX wiring).
            if(rxAddr != 0 && rxWords != 0 && txAddr != 0 && txWords != 0)
            {
                uint n = Math.Min(rxWords, txWords) * 4;
                var data = Sysbus.ReadBytes(txAddr, (int)n);
                Sysbus.WriteBytes(data, rxAddr);
            }
            else if(rxAddr != 0 && rxWords != 0)
            {
                Sysbus.WriteBytes(new byte[rxWords * 4], rxAddr);
            }
            if((cfg & 0x01u) != 0 && (ctl & 0x10u) != 0)
            {
                registers[0x20C] = registers.TryGetValue(0x20C, out var rs) ? rs | 0x2u : 0x2u; // RXDMACPL
                registers[0x304] = registers.TryGetValue(0x304, out var ri) ? ri | 0x10u : 0x10u; // RXDMACPL
            }
            if((cfg & 0x10u) != 0 && (ctl & 0x01u) != 0)
            {
                registers[0x218] = registers.TryGetValue(0x218, out var ts) ? ts | 0x2u : 0x2u; // TXDMACPL
                registers[0x304] = registers.TryGetValue(0x304, out var ti) ? ti | 0x8u : 0x8u; // TXDMACPL
            }
            UpdateIRQ();
        }

        private void UpdateIRQ()
        {
            uint stat = registers.TryGetValue(0x304, out var s) ? s : 0u;
            uint en = registers.TryGetValue(0x300, out var e) ? e : 0u;
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
            return offset == 0x300 || offset == 0x304 || offset == 0x308 || offset == 0x30C;
        }

        private IBusController Sysbus => machine.GetSystemBus(this);

        private readonly IMachine machine;
        private readonly Dictionary<long, uint> registers = new Dictionary<long, uint>();
        private LimitTimer dmaTimer;

        private static readonly KeyValuePair<long, uint>[] Resets = new KeyValuePair<long, uint>[]
        {
            new KeyValuePair<long, uint>(0x00, 0x00000000), // RXDATA
            new KeyValuePair<long, uint>(0x04, 0x00000000), // RXCHANID
            new KeyValuePair<long, uint>(0x08, 0x00000000), // RXFIFOSTATUS
            new KeyValuePair<long, uint>(0x0C, 0x00000000), // RXFIFOSIZE
            new KeyValuePair<long, uint>(0x10, 0x00000000), // RXUPPERLIMIT
            new KeyValuePair<long, uint>(0x20, 0x00000000), // TXDATA
            new KeyValuePair<long, uint>(0x24, 0x00000000), // TXCHANID
            new KeyValuePair<long, uint>(0x28, 0x00000000), // TXFIFOSTATUS
            new KeyValuePair<long, uint>(0x2C, 0x00000000), // TXFIFOSIZE
            new KeyValuePair<long, uint>(0x30, 0x00000000), // TXLOWERLIMIT
            new KeyValuePair<long, uint>(0x40, 0x01AC01A4), // I2SDATACFG
            new KeyValuePair<long, uint>(0x44, 0x01F103F0), // I2SIOCFG
            new KeyValuePair<long, uint>(0x48, 0x00000000), // I2SCTL
            new KeyValuePair<long, uint>(0x4C, 0x00000000), // IPBIRPT
            new KeyValuePair<long, uint>(0x50, 0xDA1A0000), // IPCOREID
            new KeyValuePair<long, uint>(0x54, 0x00000002), // AMQCFG
            new KeyValuePair<long, uint>(0x60, 0x00000000), // INTDIV
            new KeyValuePair<long, uint>(0x64, 0x00000000), // FRACDIV
            new KeyValuePair<long, uint>(0x100, 0x00000160), // CLKCFG
            new KeyValuePair<long, uint>(0x200, 0x00000000), // DMACFG
            new KeyValuePair<long, uint>(0x204, 0x00000000), // RXDMATOTCNT
            new KeyValuePair<long, uint>(0x208, 0x00000000), // RXDMAADDR
            new KeyValuePair<long, uint>(0x20C, 0x00000000), // RXDMASTAT
            new KeyValuePair<long, uint>(0x210, 0x00000000), // TXDMATOTCNT
            new KeyValuePair<long, uint>(0x214, 0x00000000), // TXDMAADDR
            new KeyValuePair<long, uint>(0x218, 0x00000000), // TXDMASTAT
            new KeyValuePair<long, uint>(0x21C, 0x00000000), // DMAENNEXTCTRL
            new KeyValuePair<long, uint>(0x220, 0x00000000), // RXDMATOTCNTNEXT
            new KeyValuePair<long, uint>(0x224, 0x00000000), // RXDMAADDRNEXT
            new KeyValuePair<long, uint>(0x228, 0x00000000), // TXDMATOTCNTNEXT
            new KeyValuePair<long, uint>(0x22C, 0x00000000), // TXDMAADDRNEXT
            new KeyValuePair<long, uint>(0x230, 0x00000000), // STATUS
            new KeyValuePair<long, uint>(0x300, 0x00000000), // INTEN
            new KeyValuePair<long, uint>(0x304, 0x00000000), // INTSTAT
            new KeyValuePair<long, uint>(0x308, 0x00000000), // INTCLR
            new KeyValuePair<long, uint>(0x30C, 0x00000000), // INTSET
        };
    }
}
