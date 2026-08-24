//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    // Ambiq GPIO model used for both the Apollo4 and the Apollo510B. The register
    // map follows the Apollo510B CMSIS (apollo510.h) layout: 224 per-pin config
    // registers (PINCFG[0..223] @ 0x0-0x37C), a PADKEY unlock register @ 0x400,
    // seven 32-pin functional banks (RD/WT/WTS/WTC/EN/ENS/ENC @ 0x404-0x4C4) and the
    // multicore-unit interrupt registers @ 0x530-0x60C. Total block size is 0x610.
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo4_GPIO : BaseGPIOPort, IProvidesRegisterCollection<DoubleWordRegisterCollection>, IDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo4_GPIO(IMachine machine) : base(machine, NumberOfPins)
        {
            for(var i = 0; i < NumberOfExternalInterrupts; i++)
            {
                irq[i] = new GPIO();
            }

            RegistersCollection = new DoubleWordRegisterCollection(this);

            DefineRegisters();
            Reset();
        }

        public override void Reset()
        {
            base.Reset();
            RegistersCollection.Reset();

            outputPinValues.Initialize();
            tristatePinOutputEnabled.Initialize();
        }

        public override void OnGPIO(int number, bool value)
        {
            if(!CheckPinNumber(number))
            {
                return;
            }

            var oldState = State[number];
            base.OnGPIO(number, value);

            if(inputEnable[number].Value && oldState != value)
            {
                HandlePinStateChangeInterrupt(number, risingEdge: value);
            }
        }

        public uint ReadDoubleWord(long offset)
        {
            return RegistersCollection.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            // PINCFG[0..NumberOfPins-1] is write-locked until PADKEY @ 0x400 holds the unlock key.
            if(offset >= (long)Registers.PinConfiguration0 && offset <= LastPinConfigOffset)
            {
                if(padKey.Value != PadKeyUnlockValue)
                {
                    this.Log(LogLevel.Warning, "Tried to change pin configuration register which is locked. PADKEY value: {0:X}", padKey.Value);
                    return;
                }
            }

            RegistersCollection.Write(offset, value);
        }

        public long Size => 0x610;

        // MCU multicore-unit GPIO interrupt lines (N0 banks 0-6 + N1 banks 0-6).
        public GPIO McuN0IrqBank0 => irq[(int)IrqType.McuN0IrqBank0];

        public GPIO McuN0IrqBank1 => irq[(int)IrqType.McuN0IrqBank1];

        public GPIO McuN0IrqBank2 => irq[(int)IrqType.McuN0IrqBank2];

        public GPIO McuN0IrqBank3 => irq[(int)IrqType.McuN0IrqBank3];

        public GPIO McuN0IrqBank4 => irq[(int)IrqType.McuN0IrqBank4];

        public GPIO McuN0IrqBank5 => irq[(int)IrqType.McuN0IrqBank5];

        public GPIO McuN0IrqBank6 => irq[(int)IrqType.McuN0IrqBank6];

        public GPIO McuN1IrqBank0 => irq[(int)IrqType.McuN1IrqBank0];

        public GPIO McuN1IrqBank1 => irq[(int)IrqType.McuN1IrqBank1];

        public GPIO McuN1IrqBank2 => irq[(int)IrqType.McuN1IrqBank2];

        public GPIO McuN1IrqBank3 => irq[(int)IrqType.McuN1IrqBank3];

        public DoubleWordRegisterCollection RegistersCollection { get; }

        private long LastPinConfigOffset => ((long)(NumberOfPins - 1)) * 4L;

        private void DefineRegisters()
        {
            // PINCFG[0..223] @ 0x0-0x37C (per-pin configuration, write-gated by PADKEY).
            Registers.PinConfiguration0.DefineMany(this, NumberOfPins, (register, regIdx) =>
            {
                register
                    .WithValueField(0, 4, name: $"FUNCSEL{regIdx} - Function select for GPIO pin {regIdx}")
                    .WithFlag(4, out inputEnable[regIdx], name: $"INPEN{regIdx} - Input enable for GPIO {regIdx}",
                        changeCallback: (_, newValue) => { if(State[regIdx]) HandlePinStateChangeInterrupt(regIdx, risingEdge: newValue); })
                    .WithFlag(5, out readZero[regIdx], name: $"RDZERO{regIdx} - Return 0 for read data on GPIO {regIdx}")
                    .WithEnumField(6, 2, out interruptMode[regIdx], name: $"IRPTEN{regIdx} - Interrupt enable for GPIO {regIdx}")
                    .WithEnumField(8, 2, out ioMode[regIdx], name: $"OUTCFG{regIdx} - Pin IO mode selection for GPIO pin {regIdx}",
                        changeCallback: (_, __) => UpdateOutputPinState(regIdx))
                    .WithWriteCallback((_, __) =>
                    {
                        this.Log(LogLevel.Noisy, "Pin #{0} configured to IO mode {1}, input enable: {2}", regIdx, ioMode[regIdx].Value, inputEnable[regIdx].Value);
                    })
                    .WithEnumField<DoubleWordRegister, DriveStrength>(10, 2, name: $"DS{regIdx} - Drive strength selection for GPIO {regIdx}")
                    .WithFlag(12, name: $"SR{regIdx} - Configure the slew rate")
                    .WithEnumField<DoubleWordRegister, PullUpDownConfiguration>(13, 3, name: $"PULLCFG{regIdx} - Pullup/Pulldown configuration for GPIO {regIdx}")
                    .WithEnumField<DoubleWordRegister, ChipSelectConfiguration>(16, 6, name: $"NCESRC{regIdx} - IOMSTR/MSPI N Chip Select {regIdx}, DISP control signals DE, CSX, and CS")
                    .WithEnumField<DoubleWordRegister, PolarityConfiguration>(22, 1, name: $"NCEPOL{regIdx} - Polarity select for NCE for GPIO {regIdx}")
                    .WithReservedBits(23, 2)
                    .WithFlag(25, name: $"VDDPWRSWEN{regIdx} - VDD power switch enable")
                    .WithFlag(26, name: $"FIEN{regIdx} - Force input enable active regardless of function selected")
                    .WithFlag(27, name: $"FOEN{regIdx} - Force output enable active regardless of function selected")
                    .WithReservedBits(28, 4);
            }, resetValue: 0x3 /* FUNCSEL: GPIO */);

            // PADKEY @ 0x400 — write the unlock key to allow PINCFG writes.
            Registers.PadKey.Define(this)
                .WithValueField(0, 32, out padKey, name: "PADKEY");

            // RD banks @ 0x404-0x41C (pin input state, per 32-pin bank).
            Registers.InputRead0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, FieldMode.Read, name: $"RD{regIdx} - Pin {regIdx * PinsPerBank}-{regIdx * PinsPerBank + PinsPerBank - 1} input state", valueProviderCallback: (bitIdx, _) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;

                    return readZero[pinIdx].Value || !inputEnable[pinIdx].Value
                        ? false
                        : State[pinIdx];
                });
            });

            // WT banks @ 0x420-0x438 (output data write).
            Registers.OutputWrite0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"WT{regIdx} - GPIO Output {regIdx}", writeCallback: (bitIdx, _, value) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;

                    if(outputPinValues[pinIdx] != value)
                    {
                        SetOutputPinValue(pinIdx, value);
                    }
                }, valueProviderCallback: (bitIdx, _) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    return outputPinValues[pinIdx];
                });
            });

            // WTS banks @ 0x43C-0x454 (output set).
            Registers.OutputSet0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"WTS{regIdx} - GPIO Output Set {regIdx}", writeCallback: (bitIdx, _, value) =>
                {
                    if(value)
                    {
                        SetOutputPinValue(regIdx * PinsPerBank + bitIdx, true);
                    }
                });
            });

            // WTC banks @ 0x458-0x470 (output clear).
            Registers.OutputClear0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"WTC{regIdx} - GPIO Output Clear {regIdx}", writeCallback: (bitIdx, _, value) =>
                {
                    if(value)
                    {
                        SetOutputPinValue(regIdx * PinsPerBank + bitIdx, false);
                    }
                });
            });

            // EN banks @ 0x474-0x48C (output enable).
            Registers.GPIOOutputEnable0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"EN{regIdx} - GPIO Enable {regIdx}",
                    valueProviderCallback: (bitIdx, _) => tristatePinOutputEnabled[regIdx * PinsPerBank + bitIdx],
                    writeCallback: (bitIdx, _, value) => EnableTristatePinOutputState(regIdx * PinsPerBank + bitIdx, value));
            });

            // ENS banks @ 0x490-0x4A8 (output enable set).
            Registers.GPIOOutputEnableSet0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"ENS{regIdx} - GPIO Enable Set {regIdx}",
                    valueProviderCallback: (bitIdx, _) => tristatePinOutputEnabled[regIdx * PinsPerBank + bitIdx],
                    writeCallback: (bitIdx, _, value) =>
                    {
                        if(!value)
                        {
                            return;
                        }

                        EnableTristatePinOutputState(regIdx * PinsPerBank + bitIdx, true);
                    });
            });

            // ENC banks @ 0x4AC-0x4C4 (output enable clear).
            Registers.GPIOOutputEnableClear0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"ENC{regIdx} - GPIO Enable Clear {regIdx}",
                    valueProviderCallback: (bitIdx, _) => tristatePinOutputEnabled[regIdx * PinsPerBank + bitIdx],
                    writeCallback: (bitIdx, _, value) =>
                    {
                        if(!value)
                        {
                            return;
                        }

                        EnableTristatePinOutputState(regIdx * PinsPerBank + bitIdx, false);
                    });
            });

            // IOM0-7 flow-control IRQ select @ 0x4C8-0xE4 (plain RW).
            Registers.IOM0FlowControlIRQSelect.DefineMany(this, 8, (register, regIdx) =>
                register.WithValueField(0, 32, name: $"IOM{regIdx}IRQ - IOM{regIdx} flow control IRQ select"));

            // SDIF CD/WP pad select @ 0x4E8-0x4EC.
            Registers.SDIFCDWPPadSelect.DefineMany(this, 2, (register, regIdx) =>
                register.WithValueField(0, 32, name: $"SDIF{regIdx}CDWP - SDIF CD and WP select"));

            // Observation-mode sample @ 0x4F0.
            Registers.ObservationModeSample.Define(this)
                .WithValueField(0, 32, name: "OBSDATA - GPIO observation mode sample");

            // IEOBS banks @ 0x4F4-0x50C (RO input-enable signals per pad).
            Registers.InputEnableSignals0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
                register.WithFlags(0, PinsPerBank, FieldMode.Read, name: $"IEOBS{regIdx} - Input enable signals {regIdx}", valueProviderCallback: (bitIdx, _) => inputEnable[regIdx * PinsPerBank + bitIdx].Value));

            // OEOBS banks @ 0x510-0x528 (RO output-enable signals per pad).
            Registers.OutputEnableSignals0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
                register.WithFlags(0, PinsPerBank, FieldMode.Read, name: $"OEOBS{regIdx} - Output enable signals {regIdx}", valueProviderCallback: (bitIdx, _) => tristatePinOutputEnabled[regIdx * PinsPerBank + bitIdx]));

            // Reserved @ 0x52C.
            Registers.Reserved1.Define(this).WithReservedBits(0, 32);

            // MCU multicore-unit interrupt registers. For each MCU core there are seven
            // banks (pins in groups of 32), each bank holding EN/STAT/CLR/SET contiguously:
            //   N0: @ 0x530-0x59C, N1: @ 0x5A0-0x60C.
            Registers.MCUInterruptN0Enable0.DefineMany(this, NumberOfBanks * 4, (register, regIdx) => DefineInterruptRegister(register, regIdx, n1Priority: false));
            Registers.MCUInterruptN1Enable0.DefineMany(this, NumberOfBanks * 4, (register, regIdx) => DefineInterruptRegister(register, regIdx, n1Priority: true));
        }

        private void DefineInterruptRegister(DoubleWordRegister register, int regIdx, bool n1Priority)
        {
            var bank = regIdx / 4;
            var irqType = n1Priority ? (int)IrqType.McuN1IrqBank0 + bank : (int)IrqType.McuN0IrqBank0 + bank;

            switch(regIdx % 4)
            {
                case 0: // ENABLE
                    register.WithFlags(0, PinsPerBank, out irqEnabled[irqType])
                        .WithWriteCallback((_, __) => UpdateInterrupt((IrqType)irqType));
                    break;
                case 1: // STATUS (read-only)
                    register.WithFlags(0, PinsPerBank, out irqStatus[irqType], FieldMode.Read);
                    break;
                case 2: // CLEAR (write-1-to-clear)
                    register.WithFlags(0, PinsPerBank, writeCallback: (bitIdx, _, value) =>
                        {
                            if(value)
                            {
                                irqStatus[irqType][bitIdx].Value = false;
                            }
                        })
                        .WithWriteCallback((_, __) => UpdateInterrupt((IrqType)irqType));
                    break;
                case 3: // SET (write-1-to-set)
                    register.WithFlags(0, PinsPerBank, writeCallback: (bitIdx, _, value) =>
                        {
                            if(value)
                            {
                                irqStatus[irqType][bitIdx].Value = true;
                            }
                        })
                        .WithWriteCallback((_, __) => UpdateInterrupt((IrqType)irqType));
                    break;
            }
        }

        private void EnableTristatePinOutputState(int pinIdx, bool enabled)
        {
            tristatePinOutputEnabled[pinIdx] = enabled;
            this.Log(LogLevel.Debug, "GPIO #{0} tri-state {1}", pinIdx, enabled ? "enabled" : "disabled");

            UpdateOutputPinState(pinIdx);
        }

        private void HandlePinStateChangeInterrupt(int pinIdx, bool risingEdge)
        {
            var mode = interruptMode[pinIdx].Value;
            if(mode == InterruptEnable.EnabledOnAnyTransition
                || (risingEdge && mode == InterruptEnable.EnabledOnRisingEdgeTransition)
                || (!risingEdge && mode == InterruptEnable.EnabledOnFallingEdgeTransition))
            {
                this.Log(LogLevel.Noisy, "Triggering IRQ #{0} on the {1} edge", pinIdx, risingEdge ? "rising" : "falling");
                TriggerInterrupt(pinIdx);
            }
        }

        private bool IsPinOutputEnabled(int pinIdx)
        {
            switch(ioMode[pinIdx].Value)
            {
            case IOMode.OutputDisabled:
                return false;

            case IOMode.PushPullOutputMode:
                return true;

            case IOMode.OpenDrainOutputMode:
                return false;

            case IOMode.TristatePushPullOutputMode:
                return tristatePinOutputEnabled[pinIdx];

            default:
                throw new ArgumentException($"Unexpected IOMode: {ioMode[pinIdx].Value}");
            }
        }

        private void SetOutputPinValue(int pinIdx, bool state)
        {
            outputPinValues[pinIdx] = state;
            UpdateOutputPinState(pinIdx);
        }

        private void TriggerInterrupt(int pinIdx)
        {
            var irqBank = pinIdx / PinsPerBank;
            var banksPinOffset = pinIdx % PinsPerBank;

            TriggerInterruptInner(irqBank, banksPinOffset);
            TriggerInterruptInner(irqBank, banksPinOffset, n1Priority: true);
        }

        private void TriggerInterruptInner(int irqBank, int banksPinOffset, bool n1Priority = false)
        {
            var irqType = n1Priority ? (int)IrqType.McuN1IrqBank0 + irqBank : (int)IrqType.McuN0IrqBank0 + irqBank;
            if(irqEnabled[irqType][banksPinOffset].Value)
            {
                irqStatus[irqType][banksPinOffset].Value = true;
                UpdateInterrupt((IrqType)irqType);
            }
        }

        private void UpdateInterrupt(IrqType irqType)
        {
            var flag = false;
            for(var banksPinOffset = 0; banksPinOffset < PinsPerBank; banksPinOffset++)
            {
                if(irqEnabled[(int)irqType][banksPinOffset].Value && irqStatus[(int)irqType][banksPinOffset].Value)
                {
                    flag = true;
                    break;
                }
            }

            this.Log(LogLevel.Debug, "{0} {1} interrupt", flag ? "Setting" : "Clearing", irqType);
            irq[(int)irqType].Set(flag);
        }

        private void UpdateOutputPinState(int pinIdx)
        {
            var newPinState = IsPinOutputEnabled(pinIdx) && outputPinValues[pinIdx];
            if(Connections[pinIdx].IsSet != newPinState)
            {
                Connections[pinIdx].Set(newPinState);
                this.Log(LogLevel.Debug, "{0} output pin #{1}", newPinState ? "Setting" : "Clearing", pinIdx);
            }
        }

        private IValueRegisterField padKey;

        private readonly IFlagRegisterField[] inputEnable = new IFlagRegisterField[NumberOfPins];
        private readonly IEnumRegisterField<InterruptEnable>[] interruptMode = new IEnumRegisterField<InterruptEnable>[NumberOfPins];
        private readonly IEnumRegisterField<IOMode>[] ioMode = new IEnumRegisterField<IOMode>[NumberOfPins];
        private readonly IFlagRegisterField[][] irqEnabled = new IFlagRegisterField[NumberOfExternalInterrupts][];
        private readonly IFlagRegisterField[][] irqStatus = new IFlagRegisterField[NumberOfExternalInterrupts][];
        private readonly IFlagRegisterField[] readZero = new IFlagRegisterField[NumberOfPins];

        private readonly GPIO[] irq = new GPIO[NumberOfExternalInterrupts];
        private readonly bool[] outputPinValues = new bool[NumberOfPins];
        private readonly bool[] tristatePinOutputEnabled = new bool[NumberOfPins];

        private const int NumberOfBanks = NumberOfPins / PinsPerBank;
        private const int NumberOfExternalInterrupts = 2 * NumberOfBanks; // N0 banks 0-6 + N1 banks 0-6
        private const int NumberOfPins = 224;
        private const uint PadKeyUnlockValue = 0x73;
        private const int PinsPerBank = 32;

        private enum ChipSelectConfiguration
        {
            IOM0CE0 = 0x0, // IOM 0 NCE 0 module
            IOM0CE1 = 0x1, // IOM 0 NCE 1 module
            IOM0CE2 = 0x2, // IOM 0 NCE 2 module
            IOM0CE3 = 0x3, // IOM 0 NCE 3 module
            IOM1CE0 = 0x4, // IOM 1 NCE 0 module
            IOM1CE1 = 0x5, // IOM 1 NCE 1 module
            IOM1CE2 = 0x6, // IOM 1 NCE 2 module
            IOM1CE3 = 0x7, // IOM 1 NCE 3 module
            IOM2CE0 = 0x8, // IOM 2 NCE 0 module
            IOM2CE1 = 0x9, // IOM 2 NCE 1 module
            IOM2CE2 = 0xA, // IOM 2 NCE 2 module
            IOM2CE3 = 0xB, // IOM 2 NCE 3 module
            IOM3CE0 = 0xC, // IOM 3 NCE 0 module
            IOM3CE1 = 0xD, // IOM 3 NCE 1 module
            IOM3CE2 = 0xE, // IOM 3 NCE 2 module
            IOM3CE3 = 0xF, // IOM 3 NCE 3 module
            IOM4CE0 = 0x10, // IOM 4 NCE 0 module
            IOM4CE1 = 0x11, // IOM 4 NCE 1 module
            IOM4CE2 = 0x12, // IOM 4 NCE 2 module
            IOM4CE3 = 0x13, // IOM 4 NCE 3 module
            IOM5CE0 = 0x14, // IOM 5 NCE 0 module
            IOM5CE1 = 0x15, // IOM 5 NCE 1 module
            IOM5CE2 = 0x16, // IOM 5 NCE 2 module
            IOM5CE3 = 0x17, // IOM 5 NCE 3 module
            IOM6CE0 = 0x18, // IOM 6 NCE 0 module
            IOM6CE1 = 0x19, // IOM 6 NCE 1 module
            IOM6CE2 = 0x1A, // IOM 6 NCE 2 module
            IOM6CE3 = 0x1B, // IOM 6 NCE 3 module
            IOM7CE0 = 0x1C, // IOM 7 NCE 0 module
            IOM7CE1 = 0x1D, // IOM 7 NCE 1 module
            IOM7CE2 = 0x1E, // IOM 7 NCE 2 module
            IOM7CE3 = 0x1F, // IOM 7 NCE 3 module
            MSPI0CEN0 = 0x20, // MSPI 0 NCE 0 module
            MSPI0CEN1 = 0x21, // MSPI 0 NCE 1 module
            MSPI1CEN0 = 0x22, // MSPI 1 NCE 0 module
            MSPI1CEN1 = 0x23, // MSPI 1 NCE 1 module
            MSPI2CEN0 = 0x24, // MSPI 2 NCE 0 module
            MSPI2CEN1 = 0x25, // MSPI 2 NCE 1 module
            DC_DPI_DE = 0x26, // DC DPI DE module
            DISP_CONT_CSX = 0x27, // DISP CONT CSX module
            DC_SPI_CS_N = 0x28, // DC SPI CS_N module
            DC_QSPI_CS_N = 0x29, // DC QSPI CS_N module
            DC_RESX = 0x2A, // DC module RESX
        }

        private enum DriveStrength
        {
            OutputDriver0_1x = 0x0, // 0.1x output driver selected
            OutputDriver0_5x = 0x1, // 0.5x output driver selected
        }

        private enum InterruptEnable
        {
            Disabled = 0x0, // Interrupts are disabled for this GPIO
            EnabledOnFallingEdgeTransition = 0x1, // Interrupts are enabled for falling edge transition on this GPIO
            EnabledOnRisingEdgeTransition = 0x2, // Interrupts are enabled for rising edge transitions on this GPIO
            EnabledOnAnyTransition = 0x3, // Interrupts are enabled for any edge transition on this GPIO
        }

        private enum IOMode
        {
            OutputDisabled = 0x0, // Output Disabled
            PushPullOutputMode = 0x1, // Output configured in push pull mode. Will drive 0 and 1 values on pin.
            OpenDrainOutputMode = 0x2, // Output configured in open drain mode. Will only drive pin low, tristate otherwise.
            TristatePushPullOutputMode = 0x3, // Output configured in Tristate-able push pull mode. Will drive 0, 1 of HiZ on pin.
        }

        private enum IrqType
        {
            McuN0IrqBank0,
            McuN0IrqBank1,
            McuN0IrqBank2,
            McuN0IrqBank3,
            McuN0IrqBank4,
            McuN0IrqBank5,
            McuN0IrqBank6,
            McuN1IrqBank0,
            McuN1IrqBank1,
            McuN1IrqBank2,
            McuN1IrqBank3,
            McuN1IrqBank4,
            McuN1IrqBank5,
            McuN1IrqBank6
        }

        private enum PolarityConfiguration
        {
            ActiveLow = 0x0, // Polarity is active low
            ActiveHigh = 0x1, // Polarity is active high
        }

        private enum PullUpDownConfiguration
        {
            None = 0x0, // No pullup or pulldown selected
            Pulldown50K = 0x1, // 50K Pulldown selected
            Pullup1_5K = 0x2, // 1.5K Pullup selected
            Pullup6K = 0x3, // 6K Pullup selected
            Pullup12K = 0x4, // 12K Pullup selected
            Pullup24K = 0x5, // 24K Pullup selected
            Pullup50K = 0x6, // 50K Pullup selected
            Pullup100K = 0x7, // 100K Pullup selected
        }

        private enum Registers : long
        {
            PinConfiguration0 = 0x000, // Configuration control for GPIO pin 0 (PINCFG[0..223] @ 0x0-0x37C)
            PadKey = 0x400, // Key register; write 0x73 to unlock PINCFG
            InputRead0 = 0x404, // RD banks @ 0x404-0x41C (pin input state per 32-pin bank)
            OutputWrite0 = 0x420, // WT banks @ 0x420-0x438 (output data write)
            OutputSet0 = 0x43C, // WTS banks @ 0x43C-0x454 (output set)
            OutputClear0 = 0x458, // WTC banks @ 0x458-0x470 (output clear)
            GPIOOutputEnable0 = 0x474, // EN banks @ 0x474-0x48C (output enable)
            GPIOOutputEnableSet0 = 0x490, // ENS banks @ 0x490-0x4A8 (enable set)
            GPIOOutputEnableClear0 = 0x4AC, // ENC banks @ 0x4AC-0x4C4 (enable clear)
            IOM0FlowControlIRQSelect = 0x4C8, // IOM0-7 flow control IRQ select @ 0x4C8-0xE4
            SDIFCDWPPadSelect = 0x4E8, // SDIF CD/WP pad select @ 0x4E8/0x4EC
            ObservationModeSample = 0x4F0, // OBSDATA observation-mode sample
            InputEnableSignals0 = 0x4F4, // IEOBS banks @ 0x4F4-0x50C (RO input-enable signals)
            OutputEnableSignals0 = 0x510, // OEOBS banks @ 0x510-0x528 (RO output-enable signals)
            Reserved1 = 0x52C, // reserved
            MCUInterruptN0Enable0 = 0x530, // MCU N0 INT0-6 EN/STAT/CLR/SET @ 0x530-0x59C
            MCUInterruptN1Enable0 = 0x5A0, // MCU N1 INT0-6 EN/STAT/CLR/SET @ 0x5A0-0x60C
        }
    }
}
