//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Derived from Ambiq Apollo4 ADC model for the Apollo510, adding DMA transfer support.
//
using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Sensor;
using Antmicro.Renode.Peripherals.Timers;
using Antmicro.Renode.Time;
using Antmicro.Renode.Utilities.RESD;

namespace Antmicro.Renode.Peripherals.Analog
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510_ADC : BasicDoubleWordPeripheral, IKnownSize, IADC
    {
        public AmbiqApollo510_ADC(IMachine machine) : base(machine)
        {
            this.machine = machine;
            fifo = new Queue<FifoEntry>();
            interruptStatuses = new bool[InterruptsCount];
            IRQ = new GPIO();

            slots = new Slot[SlotsCount];
            for(int slotNumber = 0; slotNumber < SlotsCount; slotNumber++)
            {
                slots[slotNumber] = new Slot(slotNumber);
            }

            ADCContainer = new SimpleContainerHelper<IRESDSampleSource<VoltageSample>>(machine, this);

            DefineRegisters();
            Reset();

            this.RegisterDefaultChildren(machine);
        }

        public override void Reset()
        {
            base.Reset();

            StopIrtt();
            fifo.Clear();
            for(int interruptNumber = 0; interruptNumber < InterruptsCount; interruptNumber++)
            {
                interruptStatuses[interruptNumber] = false;
            }
        }

        public void ScanAllSlots()
        {
            this.Log(LogLevel.Noisy, "Scanning all enabled slots...");
            foreach(var slot in slots)
            {
                if(slot.IsEnabled)
                {
                    var channelNumber = (int)slot.ChannelSelect.Value;
                    if(TryGetDataFromChannel(channelNumber, out var data))
                    {
                        PushToFifo(data, (uint)slot.Number);
                    }
                }
            }
        }

        public override void WriteDoubleWord(long offset, uint value)
        {
            switch((Registers)offset)
            {
            case Registers.Configuration:
            case Registers.Slot0Configuration:
            case Registers.Slot1Configuration:
            case Registers.Slot2Configuration:
            case Registers.Slot3Configuration:
            case Registers.Slot4Configuration:
            case Registers.Slot5Configuration:
            case Registers.Slot6Configuration:
            case Registers.Slot7Configuration:
                // Only the configuration changes which stop ADC are allowed if it's enabled.
                var writeStopsTheModule = (Registers)offset == Registers.Configuration && (value & 1) == 0;
                if(!moduleEnabled.Value || writeStopsTheModule)
                {
                    break;
                }
                this.Log(LogLevel.Warning, "{0}: Ignoring the write with value: 0x{1:X}; the module has to be disabled first.", (Registers)offset, value);
                return;
            }
            base.WriteDoubleWord(offset, value);
        }

        public GPIO IRQ { get; }

        public long Size => 0x294;

        public int ADCChannelCount => 12;

        public SimpleContainerHelper<IRESDSampleSource<VoltageSample>> ADCContainer { get; private set; }

        private void DefineRegisters()
        {
            Registers.Configuration.Define(this)
                .WithFlag(0, out moduleEnabled, name: "ADCEN", writeCallback: (oldValue, newValue) => { if(oldValue && !newValue) { fifo.Clear(); StopIrtt(); } else if(newValue && !oldValue) { TryUpdateIrtt(); } })
                .WithIgnoredBits(1, 1)
                .WithFlag(2, out repeatEnabled, name: "RPTEN", writeCallback: (_, __) => TryUpdateIrtt())
                .WithFlag(3, name: "LPMODE")
                .WithFlag(4, name: "CKMODE")
                .WithIgnoredBits(5, 7)
                .WithFlag(12, out fifoPushEnabled, name: "DFIFORDEN")
                .WithIgnoredBits(13, 3)
                .WithValueField(16, 3, name: "TRIGSEL")
                .WithFlag(19, name: "TRIGPOL")
                .WithFlag(20, out repeatIntTrigger, name: "RPTTRIGSEL", writeCallback: (_, __) => TryUpdateIrtt())
                .WithIgnoredBits(21, 3)
                .WithValueField(24, 2, name: "CLKSEL")
                .WithIgnoredBits(26, 6)
                ;

            Registers.PowerStatus.Define(this)
                .WithFlag(0, name: "PWDSTAT")
                .WithIgnoredBits(1, 31)
                ;

            Registers.SoftwareTrigger.Define(this)
                .WithValueField(0, 8, FieldMode.Write, name: "SWT", writeCallback: (_, newValue) =>
                {
                    // Writing the magic value initiates a scan regardless of the Trigger Select field in the Configuration register.
                    if(newValue == SoftwareTriggerMagicValue)
                    {
                        ScanAllSlots();
                    }
                })
                .WithIgnoredBits(8, 24)
                ;

            Registers.Slot0Configuration.Define32Many(this, SlotsCount, (register, index) =>
                {
                    register
                        .WithFlag(0, out slots[index].EnableFlag, name: $"SLEN{index}")
                        .WithFlag(1, name: $"WCEN{index}")
                        .WithIgnoredBits(2, 6)
                        .WithEnumField(8, 4, out slots[index].ChannelSelect, name: $"CHSEL{index}", writeCallback: (oldValue, newValue) =>
                        {
                            if((int)newValue >= ChannelsCount)
                            {
                                this.Log(LogLevel.Error, "Slot{0}: Invalid channel select: {1}; the previous value will be kept: {2}",
                                        index, newValue, oldValue);
                                slots[index].ChannelSelect.Value = oldValue;
                            }
                        })
                        .WithIgnoredBits(12, 4)
                        .WithValueField(16, 2, name: $"PRMODE{index}")
                        .WithValueField(18, 6, name: $"TRKCYC{index}")
                        .WithValueField(24, 3, name: $"ADSEL{index}")
                        .WithIgnoredBits(27, 5)
                        ;
                });

            Registers.WindowComparatorUpperLimits.Define(this)
                .WithValueField(0, 20, name: "ULIM")
                .WithIgnoredBits(20, 12)
                ;

            Registers.WindowComparatorLowerLimits.Define(this)
                .WithValueField(0, 20, name: "LLIM")
                .WithIgnoredBits(20, 12)
                ;

            Registers.ScaleWindowComparatorLimits.Define(this)
                .WithFlag(0, name: "SCWLIMEN")
                .WithIgnoredBits(1, 31)
                ;

            Registers.Fifo.Define(this)
                .WithValueField(0, 20, name: "DATA", valueProviderCallback: _ => fifo.Count > 0 ? fifo.Peek().Data : 0x0)
                .WithValueField(20, 8, name: "COUNT", valueProviderCallback: _ => (uint)fifo.Count)
                .WithValueField(28, 3, name: "SLOTNUM", valueProviderCallback: _ => fifo.Count > 0 ? fifo.Peek().SlotNumber : 0x0)
                .WithFlag(31, name: "RSVD")
                // Writing FIFO register with any value causes the Pop to occur.
                .WithWriteCallback((__, ___) => { if(fifo.Count > 0) _ = fifo.Dequeue(); })
                ;

            Registers.FifoPopRead.Define(this)
                .WithValueField(0, 20, FieldMode.Read, name: "DATA",
                        valueProviderCallback: _ => (fifoPushEnabled.Value && fifo.Count > 0) ? fifo.Peek().Data : 0x0)
                .WithValueField(20, 8, FieldMode.Read, name: "COUNT", valueProviderCallback: _ => (uint)fifo.Count)
                .WithValueField(28, 3, FieldMode.Read, name: "SLOTNUMPR",
                        valueProviderCallback: _ => (fifoPushEnabled.Value && fifo.Count > 0) ? fifo.Peek().SlotNumber : 0x0)
                .WithFlag(31, name: "RSVDPR")
                // Reading FIFOPR register causes the Pop to occur if it's enabled in the Configuration register.
                .WithReadCallback((__, ___) => { if(fifoPushEnabled.Value && fifo.Count > 0) _ = fifo.Dequeue(); })
                ;

            Registers.InternalTimerConfiguration.Define(this)
                .WithValueField(0, 10, out irttMax, name: "TIMERMAX", writeCallback: (_, __) => TryUpdateIrtt())
                .WithIgnoredBits(10, 6)
                .WithValueField(16, 3, out irttClkDiv, name: "CLKDIV", writeCallback: (_, __) => TryUpdateIrtt())
                .WithIgnoredBits(19, 12)
                .WithFlag(31, out irttEnabled, name: "TIMEREN", writeCallback: (_, __) => TryUpdateIrtt())
                ;

            Registers.ZeroCrossingComparatorConfiguration.Define(this)
                .WithFlag(0, name: "ZXEN")
                .WithIgnoredBits(1, 3)
                .WithFlag(4, name: "ZXCHANSEL")
                .WithIgnoredBits(5, 27)
                ;

            Registers.ZeroCrossingComparatorLimits.Define(this)
                .WithValueField(0, 12, name: "LZXC")
                .WithIgnoredBits(12, 4)
                .WithValueField(16, 12, name: "UZXC")
                .WithIgnoredBits(28, 4)
                ;

            Registers.PGAGainConfiguration.Define(this)
                .WithFlag(0, name: "PGACTRLEN")
                .WithIgnoredBits(1, 3)
                .WithFlag(4, name: "UPDATEMODE")
                .WithIgnoredBits(5, 27)
                ;

            Registers.PGAGainCodes.Define(this)
                .WithValueField(0, 7, name: "LGA")
                .WithIgnoredBits(7, 1)
                .WithValueField(8, 7, name: "HGADELTA")
                .WithIgnoredBits(15, 1)
                .WithValueField(16, 7, name: "LGB")
                .WithIgnoredBits(23, 1)
                .WithValueField(24, 7, name: "HGBDELTA")
                .WithIgnoredBits(31, 1)
                ;

            Registers.SaturationComparatorConfiguration.Define(this)
                .WithFlag(0, name: "SATEN")
                .WithIgnoredBits(1, 3)
                .WithFlag(4, name: "SATCHANSEL")
                .WithIgnoredBits(5, 27)
                ;

            Registers.SaturationComparatorLimits.Define(this)
                .WithValueField(0, 12, name: "LSATC")
                .WithIgnoredBits(12, 4)
                .WithValueField(16, 12, name: "USATC")
                .WithIgnoredBits(28, 4)
                ;

            Registers.SaturationComparatorEventCounterLimits.Define(this, 0x00010001)
                .WithValueField(0, 12, name: "SATCAMAX")
                .WithIgnoredBits(12, 4)
                .WithValueField(16, 12, name: "SATCBMAX")
                .WithIgnoredBits(28, 4)
                ;

            Registers.SaturationComparatorEventCounterClear.Define(this)
                .WithFlag(0, name: "SATCACLR")
                .WithFlag(1, name: "SATCBCLR")
                .WithIgnoredBits(2, 30)
                ;

            Registers.InterruptEnable.Define(this)
                .WithFlags(0, 12, out interruptEnableFlags, name: "INTENx")
                .WithIgnoredBits(12, 20)
                .WithChangeCallback((_, __) => UpdateIRQ())
                ;

            Registers.InterruptStatus.Define(this)
                .WithFlags(0, 12, FieldMode.Read, name: "INTSTATx", valueProviderCallback: (interrupt, _) => interruptStatuses[interrupt])
                .WithIgnoredBits(12, 20)
                ;

            Registers.InterruptClear.Define(this)
                .WithFlags(0, 12, FieldMode.Write, name: "INTCLRx",
                        writeCallback: (interrupt, _, newValue) => { if(newValue) SetInterruptStatus((Interrupts)interrupt, false); })
                .WithIgnoredBits(12, 20)
                ;

            Registers.InterruptSet.Define(this)
                .WithFlags(0, 12, FieldMode.Write, name: "INTSETx",
                        writeCallback: (interrupt, _, newValue) => { if(newValue) SetInterruptStatus((Interrupts)interrupt, true); })
                .WithIgnoredBits(12, 20)
                ;

            Registers.DMATriggerEnable.Define(this)
                .WithFlag(0, name: "DFIFO75")
                .WithFlag(1, name: "DFIFOFULL")
                .WithIgnoredBits(2, 30)
                ;

            Registers.DMATriggerStatus.Define(this)
                .WithFlag(0, name: "D75STAT")
                .WithFlag(1, name: "DFULLSTAT")
                .WithIgnoredBits(2, 30)
                ;

            Registers.DMAConfiguration.Define(this)
                .WithValueField(0, 1, out dmaEn, name: "DMAEN")
                .WithIgnoredBits(1, 1)
                .WithFlag(2, name: "DMADIR")
                .WithIgnoredBits(3, 5)
                .WithFlag(8, name: "DMAPRI")
                .WithFlag(9, name: "DMADYNPRI")
                .WithIgnoredBits(10, 7)
                .WithFlag(17, name: "DMAMSK")
                .WithFlag(18, name: "DPWROFF")
                .WithIgnoredBits(19, 13)
                .WithChangeCallback((_, __) => { if (dmaEn.Value == 1) PerformDma(); })
                ;

            Registers.DMATotalTransferCount.Define(this)
                .WithIgnoredBits(0, 2)
                .WithValueField(2, 16, out dmaTotCount, name: "TOTCOUNT")
                .WithIgnoredBits(18, 14)
                ;

            Registers.DMATargetAddress.Define(this, 0x10000000)
                .WithValueField(0, 28, out dmaTargAddrLo, name: "LTARGADDR")
                .WithValueField(28, 4, out dmaTargAddrHi, name: "UTARGADDR")
                ;

            Registers.DMAStatus.Define(this)
                .WithValueField(0, 1, FieldMode.Read, name: "DMATIP", valueProviderCallback: _ => (uint)(dmaTip ? 1 : 0))
                .WithValueField(1, 1, out dmaCplFlag, name: "DMACPL")
                .WithValueField(2, 1, FieldMode.Read, name: "DMAERR", valueProviderCallback: _ => (uint)(dmaErr ? 1 : 0))
                .WithIgnoredBits(3, 29)
                ;
        }

        private void PushToFifo(uint data, uint slotNumber)
        {
            this.Log(LogLevel.Noisy, "Data pushed to Fifo for slot#{0}: 0x{1:X}", slotNumber, data);
            fifo.Enqueue(new FifoEntry(data, slotNumber));
            SetInterruptStatus(Interrupts.ConversionComplete, true);
        }

        private void SetInterruptStatus(Interrupts interrupt, bool value)
        {
            if(interruptStatuses[(int)interrupt] != value)
            {
                this.NoisyLog("{0} interrupt status {1}", interrupt, value ? "set" : "reset");
                interruptStatuses[(int)interrupt] = value;
                UpdateIRQ();
            }
        }

        private bool TryGetDataFromChannel(int channelNumber, out uint data)
        {
            data = 0;
            this.AssertChannel(channelNumber);

            if(ADCContainer.TryGetByAddress(channelNumber, out var source))
            {
                var rawValue = source.Sample.ToADCRawValue(ReferenceVoltage, ResolutionInBits);
                data = rawValue << 6; // Bits 0-5 are for the fractional part, integer part is in MSB.
                return true;
            }
            return false;
        }

        private void UpdateIRQ()
        {
            var newStatus = false;
            for(int i = 0; i < InterruptsCount; i++)
            {
                if(interruptStatuses[i] && interruptEnableFlags[i].Value)
                {
                    newStatus = true;
                    break;
                }
            }

            if(newStatus != IRQ.IsSet)
            {
                this.NoisyLog("IRQ {0}", newStatus ? "set" : "reset");
                IRQ.Set(newStatus);
            }
        }

        private void PerformDma()
        {
            var wordCount = (int)(dmaTotCount.Value & 0xFFFF);
            if (wordCount == 0) return;

            // Reconstruct full 32-bit target address from LTARGADDR[27:0] + UTARGADDR[31:28]
            var addr = (dmaTargAddrLo.Value & 0x0FFFFFFF) | ((dmaTargAddrHi.Value & 0xF) << 28);

            dmaErr = false;
            for (int i = 0; i < wordCount; i++)
            {
                if (!fifo.TryDequeue(out var entry))
                {
                    // No more data in FIFO — fill remaining with zeros
                    sysbus.WriteDoubleWord(addr + (uint)(i * 4), 0, context: this);
                    dmaErr = true;
                    continue;
                }
                sysbus.WriteDoubleWord(addr + (uint)(i * 4), entry.Data, context: this);
            }

            if (dmaErr)
            {
                SetInterruptStatus(Interrupts.DmaErrorCondition, true);
            }
            else
            {
                dmaCplFlag.Value = 1;
                SetInterruptStatus(Interrupts.DmaTransferComplete, true);
            }

            // Auto-clear DMAEN after completion (one-shot transfer)
            dmaEn.Value = 0;
        }

        private IFlagRegisterField fifoPushEnabled;
        private IFlagRegisterField[] interruptEnableFlags;
        private IFlagRegisterField moduleEnabled;

        // Internal repeat-trigger timer (IRTT): HFRC 24MHz / CLKDIV, period
        // TIMERMAX+1 ticks. Rearms scans for REPEATING_SCAN workloads.
        private readonly IMachine machine;
        private IFlagRegisterField repeatEnabled;
        // RPTTRIGSEL: 0 = external timer (TMR), 1 = internal repeat timer (INT).
        private IFlagRegisterField repeatIntTrigger;
        private IFlagRegisterField irttEnabled;
        private IValueRegisterField irttMax;
        private IValueRegisterField irttClkDiv;
        private LimitTimer irttTimer;

        private void TryUpdateIrtt()
        {
            if(moduleEnabled != null && moduleEnabled.Value
               && repeatEnabled != null && repeatEnabled.Value
               && (repeatIntTrigger == null || repeatIntTrigger.Value)
               && irttEnabled != null && irttEnabled.Value)
            {
                uint div;
                switch(irttClkDiv.Value)
                {
                case 0: div = 1; break;
                case 1: div = 2; break;
                case 2: div = 4; break;
                case 4: div = 16; break;
                default: div = 1u << (int)irttClkDiv.Value; break;
                }
                var ticks = (ulong)irttMax.Value + 1;
                var freq = 24000000uL / div / (ticks == 0 ? 1 : ticks);
                if(freq == 0)
                {
                    freq = 1;
                }
                if(irttTimer == null)
                {
                    irttTimer = new LimitTimer(machine.ClockSource, freq, this, "adc-irtt",
                        limit: 1, direction: Direction.Ascending, enabled: true,
                        workMode: WorkMode.Periodic, eventEnabled: true, autoUpdate: true);
                    irttTimer.LimitReached += OnIrttTick;
                }
                else
                {
                    irttTimer.Frequency = freq;
                    irttTimer.Enabled = true;
                }
                this.DebugLog("ADC IRTT armed: div {0}, max {1} ({2} Hz)", div, irttMax.Value, freq);
            }
            else
            {
                StopIrtt();
            }
        }

        private void OnIrttTick()
        {
            ScanAllSlots();
        }

        private void StopIrtt()
        {
            if(irttTimer != null)
            {
                irttTimer.Enabled = false;
            }
        }

        // DMA state
        private IValueRegisterField dmaEn, dmaTotCount, dmaTargAddrLo, dmaTargAddrHi, dmaCplFlag;
        private volatile bool dmaTip = false, dmaErr = false;

        private readonly Queue<FifoEntry> fifo;
        private readonly bool[] interruptStatuses;
        private readonly Slot[] slots;

        private const int ChannelsCount = 12;
        private const int InterruptsCount = 12;
        private const int SlotsCount = 8;
        private const int SoftwareTriggerMagicValue = 0x37;
        // Values from https://contentportal.ambiq.com/documents/20123/388400/Apollo4-SoC-Datasheet.pdf
        private const ushort ResolutionInBits = 12;
        private const decimal ReferenceVoltage = 1.19m;

        private class Slot
        {
            public Slot(int number)
            {
                Number = number;
            }

            public bool IsEnabled => EnableFlag.Value;

            public int Number { get; }

            public IEnumRegisterField<Channels> ChannelSelect;
            public IFlagRegisterField EnableFlag;
        }

        private struct FifoEntry
        {
            public FifoEntry(uint data, uint slotNumber)
            {
                Data = data;
                SlotNumber = slotNumber;
            }

            public uint Data;
            public uint SlotNumber;
        }

        private enum Channels
        {
            SingleEndedExternalGPIOPad16 = 0x0,
            SingleEndedExternalGPIOPad29 = 0x1,
            SingleEndedExternalGPIOPad11 = 0x2,
            SingleEndedExternalGPIOPad31 = 0x3,
            SingleEndedExternalGPIOPad32 = 0x4,
            SingleEndedExternalGPIOPad33 = 0x5,
            SingleEndedExternalGPIOPad34 = 0x6,
            SingleEndedExternalGPIOPad35 = 0x7,
            InternalTemperatureSensor = 0x8,
            InternalVoltageDivideByThreeConnection = 0x9,
            AnalogTestmux = 0xA,
            InputVSS = 0xB,
        }

        private enum Interrupts
        {
            // For values based on multiple scans, this is set only when the average value is pushed to FIFO.
            ConversionComplete = 0,
            ScanComplete = 1,
            Fifo75PercentFull = 2,
            Fifo100PercentFull = 3,
            WindowComparatorVoltageExcursion = 4,
            WindowComparatorVoltageIncursion = 5,
            DmaTransferComplete = 6,
            DmaErrorCondition = 7,
            ZeroCrossingChannelA = 8,
            ZeroCrossingChannelB = 9,
            SaturationChannelA = 10,
            SaturationChannelB = 11,
        }

        private enum Registers : long
        {
            Configuration = 0x0,
            PowerStatus = 0x4,
            SoftwareTrigger = 0x8,
            Slot0Configuration = 0xC,
            Slot1Configuration = 0x10,
            Slot2Configuration = 0x14,
            Slot3Configuration = 0x18,
            Slot4Configuration = 0x1C,
            Slot5Configuration = 0x20,
            Slot6Configuration = 0x24,
            Slot7Configuration = 0x28,
            WindowComparatorUpperLimits = 0x2C,
            WindowComparatorLowerLimits = 0x30,
            ScaleWindowComparatorLimits = 0x34,
            Fifo = 0x38,
            FifoPopRead = 0x3C,
            InternalTimerConfiguration = 0x40,
            ZeroCrossingComparatorConfiguration = 0x60,
            ZeroCrossingComparatorLimits = 0x64,
            PGAGainConfiguration = 0x68,
            PGAGainCodes = 0x6C,
            SaturationComparatorConfiguration = 0xA4,
            SaturationComparatorLimits = 0xA8,
            SaturationComparatorEventCounterLimits = 0xAC,
            SaturationComparatorEventCounterClear = 0xB0,
            InterruptEnable = 0x200,
            InterruptStatus = 0x204,
            InterruptClear = 0x208,
            InterruptSet = 0x20C,
            DMATriggerEnable = 0x240,
            DMATriggerStatus = 0x244,
            DMAConfiguration = 0x280,
            DMATotalTransferCount = 0x288,
            DMATargetAddress = 0x28C,
            DMAStatus = 0x290,
        }
    }
}