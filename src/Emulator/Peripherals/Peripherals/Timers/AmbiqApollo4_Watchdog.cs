//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Time;

namespace Antmicro.Renode.Peripherals.Timers
{
    public class AmbiqApollo4_Watchdog : BasicDoubleWordPeripheral, IKnownSize
    {
        public GPIO IRQ { get; } = new GPIO();

        public AmbiqApollo4_Watchdog(IMachine machine) : base(machine)
        {
            resetTimer = new LimitTimer(machine.ClockSource, 1, this, "Reset", enabled: false, eventEnabled: true, direction: Direction.Ascending, workMode: WorkMode.OneShot);
            resetTimer.LimitReached += () =>
            {
                if(resetEnabled.Value)
                {
                    this.Log(LogLevel.Warning, "Watchog reset triggered");
                    machine.RequestReset();
                }
            };
            interruptTimer = new LimitTimer(machine.ClockSource, 1, this, "Interrupt", enabled: false, eventEnabled: true, direction: Direction.Ascending, workMode: WorkMode.OneShot);
            interruptTimer.LimitReached += () =>
            {
                interruptStatus.Value = true;
                UpdateIRQ();
            };

            DefineRegisters();
        }

        public long Size => 0x400;

        public override void Reset()
        {
            base.Reset();
            resetTimer.Enabled = false;
            interruptTimer.Enabled = false;
            IRQ.Unset();
        }

        private void DefineRegisters()
        {
            Registers.Configuration.Define(this, 0xFFFF00)
                .WithFlag(0, out var timerEnabled, name: "WDTEN")
                .WithFlag(1, out var interruptEnable, name: "INTEN")
                .WithFlag(2, out resetEnabled, name: "RESEN")
                .WithFlag(3, name: "DSPRESETINTEN")
                .WithIgnoredBits(4, 4)
                .WithValueField(8, 8, out var resetLimit, name: "RESVAL")
                .WithValueField(16, 8, out var interruptLimit, name: "INTVAL")
                .WithEnumField<DoubleWordRegister, ClockSelect>(24, 3, out var clockSelect, name: "CLKSEL")
                .WithReservedBits(27, 5)
                .WithChangeCallback((_, __) =>
                    {
                        var enableTimers = timerEnabled.Value;
                        var frequency = 1UL;

                        switch(clockSelect.Value)
                        {
                        case ClockSelect.Off:
                            enableTimers = false;
                            break;
                        case ClockSelect._128Hz:
                            frequency = 128 * FrequencyMultiplier;
                            break;
                        case ClockSelect._16Hz:
                            frequency = 16 * FrequencyMultiplier;
                            break;
                        case ClockSelect._1Hz:
                            frequency = 1 * FrequencyMultiplier;
                            break;
                        case ClockSelect._1_16Hz:
                            frequency = FrequencyMultiplier / 16;
                            break;
                        default:
                            this.Log(LogLevel.Error, "Invalid frequency value: {0}. Timer will be disabled", clockSelect.Value);
                            enableTimers = false;
                            break;
                        }

                        resetTimer.Frequency = frequency;
                        resetTimer.Limit = resetLimit.Value * FrequencyMultiplier;
                        resetTimer.Enabled = enableTimers && resetEnabled.Value;

                        interruptTimer.Frequency = frequency;
                        interruptTimer.Limit = Math.Max(interruptLimit.Value, 1) * FrequencyMultiplier;
                        interruptTimer.Enabled = enableTimers && interruptEnable.Value;
                    });

            Registers.Restart.Define(this)
                .WithValueField(0, 8, FieldMode.Write, writeCallback: (_, value) =>
                    {
                        if(value == WatchdogReloadValue)
                        {
                            resetTimer.ResetValue();
                            interruptTimer.ResetValue();
                        }
                    });

            Registers.Lock.Define(this)
                .WithValueField(0, 32, name: "LOCK");

            Registers.CounterValue.Define(this)
                .WithValueField(0, 8, FieldMode.Read, name: "COUNT", valueProviderCallback: _ => TimerValue);

            // WDT interrupt block (WDTIEREN/STAT/CLR/SET): single INT source.
            Registers.InterruptEnable.Define(this)
                .WithFlag(0, name: "INTEN");
            Registers.InterruptStatus.Define(this)
                .WithFlag(0, out interruptStatus, FieldMode.Read, name: "INT")
                .WithChangeCallback((_, __) => UpdateIRQ());
            Registers.InterruptClear.Define(this)
                .WithFlag(0, FieldMode.WriteOneToClear, name: "INTCLR",
                    writeCallback: (_, v) => { if(v) { interruptStatus.Value = false; UpdateIRQ(); } })
                .WithIgnoredBits(1, 31);
            Registers.InterruptSet.Define(this)
                .WithFlag(0, FieldMode.Write, name: "INTSET",
                    writeCallback: (_, v) => { if(v) { interruptStatus.Value = true; UpdateIRQ(); } })
                .WithIgnoredBits(1, 31);
        }

        private void UpdateIRQ()
        {
            IRQ.Set(interruptStatus.Value);
        }

        private ulong TimerValue
        {
            get
            {
                if(sysbus.TryGetCurrentCPU(out var cpu))
                {
                    cpu.SyncTime();
                }
                return resetTimer.Value / FrequencyMultiplier;
            }
        }

        private IFlagRegisterField resetEnabled;
        private IFlagRegisterField interruptStatus;

        private readonly LimitTimer resetTimer;
        private readonly LimitTimer interruptTimer;

        private const ulong WatchdogReloadValue = 0xB2;
        private const int FrequencyMultiplier = 16;

        private enum ClockSelect
        {
            Off = 0x0,
            _128Hz = 0x1,
            _16Hz = 0x2,
            _1Hz = 0x3,
            _1_16Hz = 0x4,
        }

        private enum Registers
        {
            Configuration       = 0x0,      // CFG
            Restart             = 0x4,      // RSTRT
            Lock                = 0x8,      // LOCK
            CounterValue        = 0xC,      // COUNT
            DSP0Configuration   = 0x10,     // DPS0CFG
            DSP0Restart         = 0x14,     // DSP0RSTRT
            DSP0Lock            = 0x18,     // DSP0LOCK
            DSP0CounterValue    = 0x1C,     // DSP1COUNT
            DSP1Configuration   = 0x20,     // DPS1CFG
            DSP1Restart         = 0x24,     // DSP1RSTRT
            DSP1Lock            = 0x28,     // DSP1LOCK
            DSP1CounterValue    = 0x2C,     // DSP1COUNT
            InterruptEnable     = 0x200,    // WDTIEREN
            InterruptStatus     = 0x204,    // WDTIERSTAT
            InterruptClear      = 0x208,    // WDTIERCLR
            InterruptSet        = 0x20C,    // WDTIERSET
            DSP0InterruptEnable = 0x210,    // DSP0IEREN
            DSP0InterruptStatus = 0x214,    // DSP0IERSTAT
            DSP0InterruptClear  = 0x218,    // DSP0IERCLR
            DSP0InterruptSet    = 0x21C,    // DSP0IERSET
            DSP1InterruptEnable = 0x220,    // DSP1IEREN
            DSP1InterruptStatus = 0x224,    // DSP1IERSTAT
            DSP1InterruptClear  = 0x228,    // DSP1IERCLR
            DSP1InterruptSet    = 0x22C,    // DSP1IERSET
        }
    }
}