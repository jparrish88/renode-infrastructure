//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Ambiq GPIO model for Apollo510 Lite (120 pads, GPIO @ 0x40012800).
// NEW file for the Lite family — does not modify AmbiqApollo510_GPIO.
//
// Register map from pack/SVD/apollo510L.svd: 120 PINCFG @ 0x0-0x1DC,
// PADKEY @ 0x400 (unlock 0x73, same as 510), four 32-pin banks
// (RD/WT/WTS/WTC/EN/ENS/ENC @ 0x404-0x470), six IOM flow-control IRQ
// selects @ 0x474-0x488, SDIF/OBS/IEOBS/OEOBS @ 0x48C-0x4B4, reserved
// @ 0x4B8-0x4BC, MCU N0 INT0-3 @ 0x4C0-0x4FC and N1 INT0-3 @ 0x500-0x53C.
// Total block size is 0x540.
//
// Lite PINCFG deltas vs 510 (SVD): NCESRC is 5 bits (IOM0-5 + display only,
// no MSPI/IOM6-7 chip-selects), TIMERSEL @ 23 (timer capture source),
// DSPULLCFG @ 28+3 (deep-sleep pull), no VDDPWRSWEN, bits 24-25 and 31
// reserved. FNCSEL kept as a plain value field (alternate-function routing
// is a board property, not modeled here).
//
// Bank 3 covers pins 96-127 but only 96-119 exist; accesses to phantom
// pins 120-127 are guarded and ignored.
//

using System;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.GPIOPort
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510L_GPIO : BaseGPIOPort, IProvidesRegisterCollection<DoubleWordRegisterCollection>, IDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_GPIO(IMachine machine) : base(machine, NumberOfPins)
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

        public long Size => 0x540;

        // MCU multicore-unit GPIO interrupt lines (N0 banks 0-3 + N1 banks 0-3).
        // Wired in the repl to nvic@56-59 (GPIO0_001F..GPIO0_607F) and
        // nvic@125-128 (GPIO1_001F..GPIO1_607F) per the Lite IRQ map.
        public GPIO McuN0IrqBank0 => irq[(int)IrqType.McuN0IrqBank0];

        public GPIO McuN0IrqBank1 => irq[(int)IrqType.McuN0IrqBank1];

        public GPIO McuN0IrqBank2 => irq[(int)IrqType.McuN0IrqBank2];

        public GPIO McuN0IrqBank3 => irq[(int)IrqType.McuN0IrqBank3];

        public GPIO McuN1IrqBank0 => irq[(int)IrqType.McuN1IrqBank0];

        public GPIO McuN1IrqBank1 => irq[(int)IrqType.McuN1IrqBank1];

        public GPIO McuN1IrqBank2 => irq[(int)IrqType.McuN1IrqBank2];

        public GPIO McuN1IrqBank3 => irq[(int)IrqType.McuN1IrqBank3];

        public DoubleWordRegisterCollection RegistersCollection { get; }

        private long LastPinConfigOffset => ((long)(NumberOfPins - 1)) * 4L;

        private void DefineRegisters()
        {
            // PINCFG[0..119] @ 0x0-0x1DC (per-pin configuration, write-gated by PADKEY).
            Registers.PinConfiguration0.DefineMany(this, NumberOfPins, (register, regIdx) =>
            {
                register
                    .WithValueField(0, 4, name: $"FNCSEL{regIdx} - Function select for GPIO pin {regIdx}")
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
                    .WithEnumField<DoubleWordRegister, ChipSelectConfiguration>(16, 5, name: $"NCESRC{regIdx} - IOMSTR N Chip Select {regIdx}, DISP control signals DE, CSX, and CS")
                    .WithEnumField<DoubleWordRegister, PolarityConfiguration>(22, 1, name: $"NCEPOL{regIdx} - Polarity select for NCE for GPIO {regIdx}")
                    .WithFlag(23, name: $"TIMERSEL{regIdx} - Timer capture source select")
                    .WithReservedBits(24, 2)
                    .WithFlag(26, name: $"FIEN{regIdx} - Force input enable active regardless of function selected")
                    .WithFlag(27, name: $"FOEN{regIdx} - Force output enable active regardless of function selected")
                    .WithValueField(28, 3, name: $"DSPULLCFG{regIdx} - Deep-sleep pull configuration")
                    .WithReservedBits(31, 1);
            }, resetValue: 0x3 /* FNCSEL: GPIO */);

            // PADKEY @ 0x400 — write the unlock key to allow PINCFG writes.
            Registers.PadKey.Define(this)
                .WithValueField(0, 32, out padKey, name: "PADKEY");

            // RD banks @ 0x404-0x410 (pin input state, per 32-pin bank).
            Registers.InputRead0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, FieldMode.Read, name: $"RD{regIdx} - Pin input state", valueProviderCallback: (bitIdx, _) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    if(!CheckPinNumber(pinIdx))
                    {
                        return false;
                    }

                    return readZero[pinIdx].Value || !inputEnable[pinIdx].Value
                        ? false
                        : State[pinIdx];
                });
            });

            // WT banks @ 0x414-0x420 (output data write).
            Registers.OutputWrite0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"WT{regIdx} - GPIO Output {regIdx}", writeCallback: (bitIdx, _, value) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    if(!CheckPinNumber(pinIdx))
                    {
                        return;
                    }

                    if(outputPinValues[pinIdx] != value)
                    {
                        SetOutputPinValue(pinIdx, value);
                    }
                }, valueProviderCallback: (bitIdx, _) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    return CheckPinNumber(pinIdx) && outputPinValues[pinIdx];
                });
            });

            // WTS banks @ 0x424-0x430 (output set).
            Registers.OutputSet0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"WTS{regIdx} - GPIO Output Set {regIdx}", writeCallback: (bitIdx, _, value) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    if(value && CheckPinNumber(pinIdx))
                    {
                        SetOutputPinValue(pinIdx, true);
                    }
                });
            });

            // WTC banks @ 0x434-0x440 (output clear).
            Registers.OutputClear0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"WTC{regIdx} - GPIO Output Clear {regIdx}", writeCallback: (bitIdx, _, value) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    if(value && CheckPinNumber(pinIdx))
                    {
                        SetOutputPinValue(pinIdx, false);
                    }
                });
            });

            // EN banks @ 0x444-0x450 (output enable).
            Registers.GPIOOutputEnable0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"EN{regIdx} - GPIO Enable {regIdx}",
                    valueProviderCallback: (bitIdx, _) =>
                    {
                        var pinIdx = regIdx * PinsPerBank + bitIdx;
                        return CheckPinNumber(pinIdx) && tristatePinOutputEnabled[pinIdx];
                    },
                    writeCallback: (bitIdx, _, value) =>
                    {
                        var pinIdx = regIdx * PinsPerBank + bitIdx;
                        if(CheckPinNumber(pinIdx))
                        {
                            EnableTristatePinOutputState(pinIdx, value);
                        }
                    });
            });

            // ENS banks @ 0x454-0x460 (output enable set).
            Registers.GPIOOutputEnableSet0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"ENS{regIdx} - GPIO Enable Set {regIdx}",
                    valueProviderCallback: (bitIdx, _) =>
                    {
                        var pinIdx = regIdx * PinsPerBank + bitIdx;
                        return CheckPinNumber(pinIdx) && tristatePinOutputEnabled[pinIdx];
                    },
                    writeCallback: (bitIdx, _, value) =>
                    {
                        var pinIdx = regIdx * PinsPerBank + bitIdx;
                        if(!value || !CheckPinNumber(pinIdx))
                        {
                            return;
                        }

                        EnableTristatePinOutputState(pinIdx, true);
                    });
            });

            // ENC banks @ 0x464-0x470 (output enable clear).
            Registers.GPIOOutputEnableClear0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
            {
                register.WithFlags(0, PinsPerBank, name: $"ENC{regIdx} - GPIO Enable Clear {regIdx}",
                    valueProviderCallback: (bitIdx, _) =>
                    {
                        var pinIdx = regIdx * PinsPerBank + bitIdx;
                        return CheckPinNumber(pinIdx) && tristatePinOutputEnabled[pinIdx];
                    },
                    writeCallback: (bitIdx, _, value) =>
                    {
                        var pinIdx = regIdx * PinsPerBank + bitIdx;
                        if(!value || !CheckPinNumber(pinIdx))
                        {
                            return;
                        }

                        EnableTristatePinOutputState(pinIdx, false);
                    });
            });

            // IOM0-5 flow-control IRQ select @ 0x474-0x488 (plain RW).
            Registers.IOM0FlowControlIRQSelect.DefineMany(this, 6, (register, regIdx) =>
                register.WithValueField(0, 32, name: $"IOM{regIdx}IRQ - IOM{regIdx} flow control IRQ select"));

            // SDIF CD/WP pad select @ 0x48C-0x490.
            Registers.SDIFCDWPPadSelect.DefineMany(this, 2, (register, regIdx) =>
                register.WithValueField(0, 32, name: $"SDIF{regIdx}CDWP - SDIF CD and WP select"));

            // Observation-mode sample @ 0x494.
            Registers.ObservationModeSample.Define(this)
                .WithValueField(0, 32, name: "OBSDATA - GPIO observation mode sample");

            // IEOBS banks @ 0x498-0x4A4 (RO input-enable signals per pad).
            Registers.InputEnableSignals0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
                register.WithFlags(0, PinsPerBank, FieldMode.Read, name: $"IEOBS{regIdx} - Input enable signals {regIdx}", valueProviderCallback: (bitIdx, _) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    return CheckPinNumber(pinIdx) && inputEnable[pinIdx].Value;
                }));

            // OEOBS banks @ 0x4A8-0x4B4 (RO output-enable signals per pad).
            Registers.OutputEnableSignals0.DefineMany(this, NumberOfBanks, (register, regIdx) =>
                register.WithFlags(0, PinsPerBank, FieldMode.Read, name: $"OEOBS{regIdx} - Output enable signals {regIdx}", valueProviderCallback: (bitIdx, _) =>
                {
                    var pinIdx = regIdx * PinsPerBank + bitIdx;
                    return CheckPinNumber(pinIdx) && tristatePinOutputEnabled[pinIdx];
                }));

            // Reserved @ 0x4B8-0x4BC.
            Registers.Reserved1.Define(this).WithReservedBits(0, 32);
            Registers.Reserved2.Define(this).WithReservedBits(0, 32);

            // MCU multicore-unit interrupt registers. For each MCU core there are
            // four banks (pins in groups of 32), each bank holding
            // EN/STAT/CLR/SET contiguously: N0 @ 0x4C0-0x4FC, N1 @ 0x500-0x53C.
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

        private const int NumberOfBanks = 4;
        private const int NumberOfExternalInterrupts = 2 * NumberOfBanks; // N0 banks 0-3 + N1 banks 0-3
        private const int NumberOfPins = 120;
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
            DC_DPI_DE = 0x18, // DC DPI DE module
            DISP_CONT_CSX = 0x19, // DISP CONT CSX module
            DC_SPI_CS_N = 0x1A, // DC SPI CS_N module
            DC_QSPI_CS_N = 0x1B, // DC QSPI CS_N module
            DC_RESX = 0x1C, // DC module RESX
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
            McuN1IrqBank0,
            McuN1IrqBank1,
            McuN1IrqBank2,
            McuN1IrqBank3
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
            PinConfiguration0 = 0x000, // PINCFG[0..119] @ 0x0-0x1DC
            PadKey = 0x400, // Key register; write 0x73 to unlock PINCFG
            InputRead0 = 0x404, // RD banks @ 0x404-0x410
            OutputWrite0 = 0x414, // WT banks @ 0x414-0x420
            OutputSet0 = 0x424, // WTS banks @ 0x424-0x430
            OutputClear0 = 0x434, // WTC banks @ 0x434-0x440
            GPIOOutputEnable0 = 0x444, // EN banks @ 0x444-0x450
            GPIOOutputEnableSet0 = 0x454, // ENS banks @ 0x454-0x460
            GPIOOutputEnableClear0 = 0x464, // ENC banks @ 0x464-0x470
            IOM0FlowControlIRQSelect = 0x474, // IOM0-5 flow control IRQ select @ 0x474-0x488
            SDIFCDWPPadSelect = 0x48C, // SDIF CD/WP pad select @ 0x48C/0x490
            ObservationModeSample = 0x494, // OBSDATA observation-mode sample
            InputEnableSignals0 = 0x498, // IEOBS banks @ 0x498-0x4A4
            OutputEnableSignals0 = 0x4A8, // OEOBS banks @ 0x4A8-0x4B4
            Reserved1 = 0x4B8, // reserved
            Reserved2 = 0x4BC, // reserved
            MCUInterruptN0Enable0 = 0x4C0, // MCU N0 INT0-3 EN/STAT/CLR/SET @ 0x4C0-0x4FC
            MCUInterruptN1Enable0 = 0x500, // MCU N1 INT0-3 EN/STAT/CLR/SET @ 0x500-0x53C
        }
    }
}
