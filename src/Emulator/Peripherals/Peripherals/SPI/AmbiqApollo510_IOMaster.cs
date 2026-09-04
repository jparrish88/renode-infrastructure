//
// Copyright (c) 2010-2023 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Derived from Ambiq Apollo4 IOMaster model for the Apollo510, adding full-duplex and DMA transfer support.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Exceptions;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.SPI
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510_IOMaster : IPeripheralContainer<ISPIPeripheral, TypedNumberRegistrationPoint<int>>, IPeripheralContainer<II2CPeripheral, TypedNumberRegistrationPoint<int>>,
        IDoubleWordPeripheral, IProvidesRegisterCollection<DoubleWordRegisterCollection>, IPeripheral, IKnownSize
    {
        // fullDuplex: when true, Write transactions also capture the slave's MISO output for each
        // sent byte into the incoming FIFO (true full-duplex). Off by default so existing platforms
        // and DMA tests keep the original half-duplex write behavior.
        public AmbiqApollo510_IOMaster(IMachine machine, bool fullDuplex = false)
        {
            this.fullDuplex = fullDuplex;

            RegistersCollection = new DoubleWordRegisterCollection(this);

            IRQ = new GPIO();

            // The countChangeAction cannot be set in the FIFO constructor.
            incomingFifo = new Fifo("incoming FIFO", this);
            incomingFifo.CountChangeAction += IncomingFifoCountChangeAction;
            outgoingFifo = new Fifo("outgoing FIFO", this);
            outgoingFifo.CountChangeAction += OutgoingFifoCountChangeAction;

            spiPeripherals = new Dictionary<int, ISPIPeripheral>();
            i2cPeripherals = new Dictionary<int, II2CPeripheral>();

            this.machine = machine;
            sysbusCtrl = machine.GetSystemBus(this);

            DefineRegisters();
            Reset();
        }

        public void Reset()
        {
            activeTransactionContinue = false;
            activeSpiSlaveSelect = 0;
            i2cSlaveAddress = 0;

            RegistersCollection.Reset();
            activeTransactionStatus.Value = Status.Idle;
            status = Status.Idle;

            IRQ.Unset();
            incomingFifo.Reset();
            outgoingFifo.Reset();
        }

        public uint ReadDoubleWord(long offset)
        {
            return RegistersCollection.Read(offset);
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            RegistersCollection.Write(offset, value);
        }

        public void Register(ISPIPeripheral peripheral, TypedNumberRegistrationPoint<int> registrationPoint)
        {
            if(registrationPoint.Address < 0 || registrationPoint.Address >= MaxSpiPeripheralsConnected)
            {
                throw new ConstructionException($"Invalid SPI peripheral ID: {registrationPoint.Address}! Only IDs from 0 to {MaxSpiPeripheralsConnected - 1} are valid.");
            }
            Register(spiPeripherals, peripheral, registrationPoint.WithType<ISPIPeripheral>());
        }

        public void Register(II2CPeripheral peripheral, TypedNumberRegistrationPoint<int> registrationPoint)
        {
            Register(i2cPeripherals, peripheral, registrationPoint.WithType<II2CPeripheral>());
        }

        public void Unregister(ISPIPeripheral peripheral)
        {
            Unregister(spiPeripherals, peripheral);
        }

        public void Unregister(II2CPeripheral peripheral)
        {
            Unregister(i2cPeripherals, peripheral);
        }

        public IEnumerable<TypedNumberRegistrationPoint<int>> GetRegistrationPoints(ISPIPeripheral peripheral)
        {
            return spiPeripherals.Keys.Select(x => new TypedNumberRegistrationPoint<int>(x, typeof(ISPIPeripheral)))
                .ToList();
        }

        public IEnumerable<TypedNumberRegistrationPoint<int>> GetRegistrationPoints(II2CPeripheral peripheral)
        {
            return i2cPeripherals.Keys.Select(x => new TypedNumberRegistrationPoint<int>(x, typeof(II2CPeripheral)))
                .ToList();
        }

        IEnumerable<IRegistered<ISPIPeripheral, TypedNumberRegistrationPoint<int>>> IPeripheralContainer<ISPIPeripheral, TypedNumberRegistrationPoint<int>>.Children =>
            spiPeripherals.Select(x => Registered.Create(x.Value, new TypedNumberRegistrationPoint<int>(x.Key, typeof(ISPIPeripheral)))).ToList();

        IEnumerable<IRegistered<II2CPeripheral, TypedNumberRegistrationPoint<int>>> IPeripheralContainer<II2CPeripheral, TypedNumberRegistrationPoint<int>>.Children =>
            i2cPeripherals.Select(x => Registered.Create(x.Value, new TypedNumberRegistrationPoint<int>(x.Key, typeof(II2CPeripheral)))).ToList();

        public GPIO IRQ { get; }

        public DoubleWordRegisterCollection RegistersCollection { get; }

        public long Size => 0x400;

        private void DefineRegisters()
        {
            Registers.OutgoingFifoAccessPort.DefineMany(this, 8, (register, index) =>
                {
                    register.WithValueField(0, 32,
                        valueProviderCallback: _ => outgoingFifo.DirectGet((uint)index),
                        writeCallback: (_, newValue) => outgoingFifo.DirectSet((uint)index, (uint)newValue));
                }, stepInBytes: 4);

            Registers.IncomingFifoAccessPort.DefineMany(this, 8, (register, index) =>
                {
                    register.WithValueField(0, 32, FieldMode.Read,
                        valueProviderCallback: _ => incomingFifo.DirectGet((uint)index));
                }, stepInBytes: 4);

            Registers.FifoSizeAndRemainingSlotsOpenValues.Define(this)
                .WithValueField(0, 8, FieldMode.Read, name: "FIFO0SIZ", valueProviderCallback: _ => outgoingFifo.BytesCount)
                .WithValueField(8, 8, FieldMode.Read, name: "FIFO0REM", valueProviderCallback: _ => outgoingFifo.BytesLeft)
                .WithValueField(16, 8, FieldMode.Read, name: "FIFO1SIZ", valueProviderCallback: _ => incomingFifo.BytesCount)
                .WithValueField(24, 8, FieldMode.Read, name: "FIFO1REM", valueProviderCallback: _ => incomingFifo.BytesLeft)
                ;

            Registers.FifoThresholdConfiguration.Define(this)
                .WithValueField(0, 6, out fifoInterruptReadThreshold, name: "FIFORTHR")
                .WithReservedBits(6, 2)
                .WithValueField(8, 6, out fifoInterruptWriteThreshold, name: "FIFOWTHR")
                .WithReservedBits(14, 18)
                .WithChangeCallback((_, __) => UpdateFifoThresholdInterruptStatus())
                ;

            Registers.FifoPop.Define(this)
                .WithValueField(0, 32, FieldMode.Read, name: "FIFODOUT", valueProviderCallback: _ =>
                {
                    // "Will advance the internal read pointer of the incoming FIFO (FIFO1) when read, if POPWR is not active.
                    // If POPWR is active, a write to this register is needed to advance the internal FIFO pointer."
                    if(!(writePopToAdvanceReadPointer.Value ? incomingFifo.TryPeek(out var value) : incomingFifo.TryPop(out value)))
                    {
                        this.Log(LogLevel.Warning, "Failed to read from incoming FIFO");
                        InterruptStatusSet(IoMasterInterrupts.ReadFifoUnderflow, true);
                    }
                    return value;
                }, writeCallback: (_, __) =>
                {
                    if(writePopToAdvanceReadPointer.Value)
                    {
                        if(!incomingFifo.TryAdvancePointer())
                        {
                            this.Log(LogLevel.Warning, "Failed to advance the internal read pointer of the incoming FIFO");
                        }
                    }
                    else
                    {
                        this.Log(LogLevel.Warning, "Tried to write the FIFOPOP register but the POPWR isn't set");
                    }
                })
                ;

            Registers.FifoPush.Define(this)
                .WithValueField(0, 32, FieldMode.Write, name: "FIFODIN", writeCallback: (_, newValue) =>
                {
                    var n = (uint)newValue;
                    this.Log(LogLevel.Info, "iom6 FIFODIN push w=0x{0:X8} bytes=[{1:X2} {2:X2} {3:X2} {4:X2}] outDepth={5}",
                        n, n & 0xFF, (n >> 8) & 0xFF, (n >> 16) & 0xFF, (n >> 24) & 0xFF, outgoingFifo.BytesCount);
                    if(!outgoingFifo.TryPush((uint)newValue))
                    {
                        this.Log(LogLevel.Warning, "Failed to write to the outgoing FIFO (value: 0x{0})", newValue);
                        InterruptStatusSet(IoMasterInterrupts.WriteFifoOverflow, true);
                    }
                })
                ;

            Registers.FifoControl.Define(this, 0x00000002)
                .WithFlag(0, out writePopToAdvanceReadPointer, name: "POPWR")
                .WithFlag(1, name: "FIFORSTN", writeCallback: (_, newValue) =>
                    {
                        // FIFORSTN is an inversed reset flag; FIFOs are reset when this flag is 0.
                        incomingFifo.ResetFlag = !newValue;
                        outgoingFifo.ResetFlag = !newValue;
                    },
                    // The value should be the same for both FIFOs so it doesn't matter which is returned here.
                    valueProviderCallback: _ => incomingFifo.ResetFlag)
                .WithReservedBits(2, 30)
                ;

            Registers.FifoPointers.Define(this)
                .WithValueField(0, 4, FieldMode.Read, name: "FIFOWPTR", valueProviderCallback: _ => outgoingFifo.Pointer)
                .WithReservedBits(4, 4)
                .WithValueField(8, 4, FieldMode.Read, name: "FIFORPTR", valueProviderCallback: _ => incomingFifo.Pointer)
                .WithReservedBits(12, 20)
                ;

            // Some software expects values written to
            // IOCLKEN, FSEL, DIVEN, LOWPER, TOTPER to be retained
            Registers.IOClockConfiguration.Define(this)
                .WithFlag(0, name: "IOCLKEN")
                .WithReservedBits(1, 7)
                .WithValueField(8, 3, name: "FSEL")
                .WithTaggedFlag("DIV3", 11)
                .WithFlag(12, name: "DIVEN")
                .WithReservedBits(13, 3)
                .WithValueField(16, 8, name: "LOWPER")
                .WithValueField(24, 8, name: "TOTPER")
                ;

            Registers.SubmoduleControl.Define(this)
                .WithFlag(0, out spiMasterEnabled, name: "SMOD0EN")
                .WithEnumField<DoubleWordRegister, SubmoduleTypes>(1, 3, FieldMode.Read, name: "SMOD0TYPE", valueProviderCallback: _ => SubmoduleTypes.SpiMaster)
                .WithFlag(4, out i2cMasterEnabled, name: "SMOD1EN")
                .WithEnumField<DoubleWordRegister, SubmoduleTypes>(5, 3, FieldMode.Read, name: "SMOD1TYPE", valueProviderCallback: _ => SubmoduleTypes.I2CMaster)
                .WithTaggedFlag("SMOD2EN", 8)
                // It should be I2SMaster_Slave (0x4), but I2S isn't currently supported so let's mark it as NotInstalled.
                .WithEnumField<DoubleWordRegister, SubmoduleTypes>(9, 3, FieldMode.Read, name: "SMOD2TYPE", valueProviderCallback: _ => SubmoduleTypes.NotInstalled)
                .WithReservedBits(12, 20)
                .WithWriteCallback((_, __) =>
                {
                    if(spiMasterEnabled.Value && i2cMasterEnabled.Value)
                    {
                        this.Log(LogLevel.Warning, "Both SPI and I2C modules have been enabled");
                        spiMasterEnabled.Value = false;
                        i2cMasterEnabled.Value = false;
                    }
                })
                ;

            Registers.CommandAndOffset.Define(this)
                .WithEnumField(0, 4, out transactionCommand, name: "CMD")
                .WithValueField(4, 3, out transactionOffsetCount, name: "OFFSETCNT")
                .WithFlag(7, out transactionContinue, name: "CONT")
                .WithValueField(8, 12, out transactionSize, name: "TSIZE")
                .WithValueField(20, 2, out spiSlaveSelect, name: "CMDSEL")
                .WithReservedBits(22, 2)
                .WithValueField(24, 8, out transactionOffsetLow, name: "OFFSETLO")
                .WithWriteCallback((_, __) =>
                {
                    this.Log(LogLevel.Info,
                            "Transaction received for #{0}; command: {1}, size: {2}, offset: <count: {3}, low=0x{4:X2}, high=0x{5:X8}>, cont: {6}, fullDup={7}",
                            PrettyPendingPeripheral, transactionCommand.Value, transactionSize.Value, transactionOffsetCount.Value,
                            transactionOffsetLow.Value, transactionOffsetHigh.Value, transactionContinue.Value, IsFullDuplex());

                    if(!spiMasterEnabled.Value && !i2cMasterEnabled.Value)
                    {
                        this.Log(LogLevel.Error, "Invalid operation; SPI/I2C Master inferfaces are disabled!");
                        return;
                    }

                    if(activeTransactionCommand.Value != Commands.None)
                    {
                        this.Log(LogLevel.Error, "Dropping the new transaction received for #{0}: {1}; {2} on {3} is still being processed.",
                            PrettyPendingPeripheral, transactionCommand.Value, activeTransactionCommand.Value, PrettyActivePeripheral);
                        return;
                    }

                    if(!IsTransactionValid(transactionCommand.Value, (uint)transactionSize.Value, (uint)transactionOffsetCount.Value,
                            (int)spiSlaveSelect.Value, out var errorMessage))
                    {
                        this.Log(LogLevel.Error, errorMessage);
                        activeTransactionStatus.Value = Status.Error;
                        status = Status.Idle;
                        InterruptStatusSet(IoMasterInterrupts.IllegalCommand);
                        return;
                    }

                    // Preserve important values for the transaction being currently processed.
                    // Only the command and size left values are accessible through registers.
                    activeTransactionCommand.Value = transactionCommand.Value;
                    activeTransactionContinue = transactionContinue.Value;
                    activeSpiSlaveSelect = (int)spiSlaveSelect.Value;
                    activeTransactionSizeLeft.Value = transactionSize.Value;

                    SendTransactionOffset((uint)transactionOffsetCount.Value);

                    if(activeTransactionSizeLeft.Value == 0)
                    {
                        // As this is 0-size TX transaction and we already send transaction offset,
                        // there is nothing more to do.
                        TryFinishTransaction();
                        return;
                    }

                    status = Status.Active;

                    while(activeTransactionSizeLeft.Value > 0)
                    {
                        if(activeTransactionCommand.Value == Commands.Read)
                        {
                            if(!incomingFifo.TryReceiveAndPush(ReceiveData))
                            {
                                this.Log(LogLevel.Debug, "Cannot push more data to the incoming FIFO; {0}B remain to complete the read command.",
                                    activeTransactionSizeLeft.Value);
                                break;
                            }
                        }
                        else if(activeTransactionCommand.Value == Commands.Write)
                        {
                            if(!outgoingFifo.TryPop(out var value))
                            {
                                this.Log(LogLevel.Debug, "No more data in the outgoing FIFO; {0}B remain to complete the write command.",
                                    activeTransactionSizeLeft.Value);
                                break;
                            }
                            if(IsFullDuplex())
                            {
                                SendDataFullDuplex(value);
                            }
                            else
                            {
                                SendData(value);
                            }
                        }  // No else because only Read and Write commands are handled after 'IsTransactionValid'.
                    }

                    if(activeTransactionSizeLeft.Value != 0)
                    {
                        activeTransactionStatus.Value = Status.Wait;
                    }
                })
                ;

            Registers.DcxControlAndCeUsageSelection.Define(this)
                .WithTag("DCXSEL", 0, 4)
                .WithTaggedFlag("DCXEN", 4)
                .WithIgnoredBits(5, 27)
                ;

            Registers.HighOrderBytesBfOffsetForIOTransaction.Define(this)
                .WithValueField(0, 32, out transactionOffsetHigh, name: "OFFSETHI")
                ;

            Registers.CommandStatus.Define(this)
                // This field is designed to hold the value written to CMD. The MSB is stated as unused.
                .WithEnumField(0, 5, out activeTransactionCommand, FieldMode.Read, name: "CCMD")
                .WithEnumField(5, 3, out activeTransactionStatus, FieldMode.Read, name: "CMDSTAT")
                .WithValueField(8, 12, out activeTransactionSizeLeft, FieldMode.Read, name: "CTSIZE")
                .WithReservedBits(20, 12)
                ;

            Registers.IOMasterInterruptsEnable.Define(this)
                .WithFlags(0, IoMasterInterruptsCount, out ioMasterInterruptsEnableFlags, name: "INTENi")
                .WithReservedBits(15, 17)
                .WithChangeCallback((_, __) => UpdateIRQ())
                ;

            Registers.IOMasterInterruptsStatus.Define(this)
                .WithFlags(0, IoMasterInterruptsCount, out ioMasterInterruptsStatusFlags, FieldMode.Read, name: "INTSTATi")
                .WithReservedBits(15, 17)
                ;

            Registers.IOMasterInterruptsClear.Define(this)
                .WithFlags(0, IoMasterInterruptsCount, FieldMode.Write, writeCallback: (interrupt, _, newValue) =>
                {
                    if(newValue)
                    {
                        InterruptStatusSet((IoMasterInterrupts)interrupt, false);
                    }
                }, name: "INTCLRi")
                // IgnoredBits not to trigger warnings when 0xFFFFFFFF is written to clear all INTs.
                .WithIgnoredBits(15, 17)
                ;

            Registers.IOMasterInterruptsSet.Define(this)
                .WithFlags(0, IoMasterInterruptsCount, FieldMode.Write, writeCallback: (interrupt, _, newValue) =>
                {
                    if(newValue)
                    {
                        InterruptStatusSet((IoMasterInterrupts)interrupt, true);
                    }
                }, name: "INTSETi")
                .WithReservedBits(15, 17)
                ;

            Registers.DmaTriggerEnable.Define(this)
                .WithTaggedFlag("DCMDCMPEN", 0)
                .WithTaggedFlag("DTHREN", 1)
                .WithIgnoredBits(2, 30)
                ;

            Registers.DmaTriggerStatus.Define(this)
                .WithTaggedFlag("DCMDCMP", 0)
                .WithTaggedFlag("DTHR", 1)
                .WithTaggedFlag("DTOTCMP", 2)
                .WithReservedBits(3, 29)
                ;

            Registers.DmaConfiguration.Define(this)
                .WithValueField(0, 1, out dmaEn, name: "DMAEN")
                .WithValueField(1, 1, out dmaDir, name: "DMADIR")
                .WithReservedBits(2, 6)
                .WithTaggedFlag("DMAPRI", 8)
                .WithTaggedFlag("DPWROFF", 9)
                .WithReservedBits(10, 22)
                .WithChangeCallback((_, __) => { if (dmaEn.Value == 1) PerformDma(); })
                ;

            Registers.DmaTotalTransferCount.Define(this)
                .WithValueField(0, 12, out dmaTotCount, name: "TOTCOUNT")
                .WithReservedBits(12, 20)
                ;

            Registers.DmaTargetAddress.Define(this)
                .WithValueField(0, 29, out dmaTargAddr, name: "TARGADDR")
                .WithReservedBits(29, 3)
                ;

            Registers.DmaStatus.Define(this)
                .WithValueField(0, 1, FieldMode.Read, name: "DMATIP", valueProviderCallback: _ => (uint)(dmaTip ? 1 : 0))
                .WithValueField(1, 1, FieldMode.Read, name: "DMACPL", valueProviderCallback: _ => (uint)(dmaCpl ? 1 : 0), writeCallback: (_, __) => dmaCpl = false)
                .WithValueField(2, 1, FieldMode.Read, name: "DMAERR", valueProviderCallback: _ => (uint)(dmaErr ? 1 : 0))
                .WithReservedBits(3, 29)
                ;

            Registers.CommandQueueConfiguration.Define(this)
                .WithFlag(0, out cqEnabled, name: "CQEN", writeCallback: (_, v) => { if(v) { ProcessCommandQueue(); } })
                .WithFlag(1, name: "CQPRI")
                .WithValueField(2, 2, name: "MSPIFLGSEL")
                .WithIgnoredBits(4, 28)
                ;

            Registers.CommandQueueTargetReadAddress.Define(this)
                .WithIgnoredBits(0, 2)
                .WithValueField(2, 27, out cqAddressField, name: "CQADDR",
                    writeCallback: (_, v) => { cqAddress = (uint)(v << 2); ProcessCommandQueue(); })
                .WithIgnoredBits(29, 3)
                ;

            Registers.CommandQueueStatus.Define(this)
                .WithFlag(0, FieldMode.Read, name: "CQTIP", valueProviderCallback: _ => cqTip)
                .WithFlag(1, FieldMode.Read, name: "CQPAUSED", valueProviderCallback: _ => IsCqPaused())
                .WithFlag(2, FieldMode.Read, name: "CQERR", valueProviderCallback: _ => cqErr)
                .WithIgnoredBits(3, 29)
                ;

            Registers.CommandQueueFlag.Define(this)
                .WithValueField(0, 16, out cqFlagsField, name: "CQFLAGS",
                    writeCallback: (_, v) => { cqSwFlags = (uint)(v & 0xFF); CheckCqPauseAndProcess(); })
                .WithValueField(16, 16, out cqIrqMaskField, name: "CQIRQMASK")
                ;

            Registers.CommandQueueFlagSetClear.Define(this)
                .WithValueField(0, 8, FieldMode.Write, name: "CQFSET",
                    writeCallback: (_, v) => { cqSwFlags |= (uint)(v & 0xFF); CheckCqPauseAndProcess(); })
                .WithValueField(8, 8, FieldMode.Write, name: "CQFTGL",
                    writeCallback: (_, v) => { cqSwFlags ^= (uint)(v & 0xFF); CheckCqPauseAndProcess(); })
                .WithValueField(16, 8, FieldMode.Write, name: "CQFCLR",
                    writeCallback: (_, v) => { cqSwFlags &= ~(uint)(v & 0xFF); CheckCqPauseAndProcess(); })
                .WithIgnoredBits(24, 8)
                ;

            Registers.CommandQueuePauseEnable.Define(this)
                .WithValueField(0, 16, out cqPauseEnField, name: "CQPEN",
                    writeCallback: (_, v) => { CheckCqPauseAndProcess(); })
                .WithIgnoredBits(16, 16)
                ;

            Registers.CommandQueueCurrentIndexValue.Define(this)
                .WithValueField(0, 8, out cqCurIdxField, name: "CQCURIDX",
                    writeCallback: (_, v) => { CheckCqPauseAndProcess(); })
                .WithIgnoredBits(8, 24)
                ;

            Registers.CommandQueueEndIndexValue.Define(this)
                .WithValueField(0, 8, out cqEndIdxField, name: "CQENDIDX",
                    writeCallback: (_, v) => { CheckCqPauseAndProcess(); })
                .WithIgnoredBits(8, 24)
                ;

            Registers.IOModuleStatus.Define(this)
                .WithTaggedFlag("ERR", 0)
                .WithFlag(1, FieldMode.Read, name: "CMDACT", valueProviderCallback: _ => status == Status.Active)
                .WithFlag(2, FieldMode.Read, name: "IDLEST", valueProviderCallback: _ => status == Status.Idle)
                .WithReservedBits(3, 29)
                ;

            Registers.SpiModuleMasterConfiguration.Define(this, 0x00200000)
                .WithTaggedFlag("SPOL", 0)
                .WithTaggedFlag("SPHA", 1)
                .WithFlag(2, out fullDuplexReg, name: "FULLDUP")
                .WithIgnoredBits(3, 13)
                .WithTaggedFlag("WTFC", 16)
                .WithTaggedFlag("RDFC", 17)
                .WithTaggedFlag("MOSIINV", 18)
                .WithIgnoredBits(19, 1)
                .WithTaggedFlag("WTFCIRQ", 20)
                .WithTaggedFlag("WTFCPOL", 21)
                .WithTaggedFlag("RDFCPOL", 22)
                .WithTaggedFlag("SPILSB", 23)
                .WithTag("DINDLY", 24, 3)
                .WithTag("DOUTDLY", 27, 3)
                .WithTaggedFlag("MSPIRST", 30)
                .WithIgnoredBits(31, 1)
                ;

            // Some software expects values written to
            // SDADLY, SCLENDLY and SDAENDLY to be retined
            Registers.I2CMasterConfiguration.Define(this)
                .WithFlag(0, out i2cExtendedAdressingMode, name: "ADDRSZ")
                .WithTaggedFlag("I2CLSB", 1)
                .WithTaggedFlag("ARBEN", 2)
                .WithReservedBits(3, 1)
                .WithValueField(4, 2, name: "SDADLY")
                .WithFlag(6, name: "MI2CRST")
                .WithReservedBits(7, 1)
                .WithValueField(8, 4, name: "SCLENDLY")
                .WithValueField(12, 4, name: "SDAENDLY")
                .WithTag("SMPCNT", 16, 8)
                .WithTaggedFlag("STRDIS", 24)
                .WithReservedBits(25, 7)
                ;

            Registers.I2CDeviceConfiguration.Define(this)
                .WithValueField(0, 10, name: "DEVADDR",
                    valueProviderCallback: _ => i2cSlaveAddress,
                    writeCallback: (_, value) =>
                    {
                        if(!i2cExtendedAdressingMode.Value && value > 0x7F)
                        {
                            value &= 0x7F;
                            this.Log(LogLevel.Warning, "Tried to set 10-bit address with extended mode disabled; truncated to 7-bit");
                        }
                        i2cSlaveAddress = (uint)value;
                    })
                .WithReservedBits(10, 22)
                ;

            Registers.I2SControl.Define(this)
                .WithTaggedFlag("I2SEN", 0)
                .WithTaggedFlag("RXTXN", 1)
                .WithTaggedFlag("CLKMS", 2)
                .WithTaggedFlag("SE", 3)
                .WithTag("CHANSIZE", 4, 5)
                .WithTag("SAMPLESIZE", 9, 5)
                .WithTag("BOFFSET", 14, 5)
                .WithTag("CHANCNT", 19, 3)
                .WithTaggedFlag("LSBFIRST", 22)
                .WithTaggedFlag("CLKGAP", 23)
                .WithTag("CTRLSPARE", 24, 8)
                ;

            Registers.I2SClockControl.Define(this)
                .WithTaggedFlag("ASRCEN", 0)
                .WithTaggedFlag("ASRCCLKSEL", 1)
                .WithTag("I2SCLKSEL", 2, 2)
                .WithTag("ASEL", 4, 3)
                .WithReservedBits(7, 25)
                ;

            Registers.I2SFrameSyncControl.Define(this)
                .WithTag("FSLEN", 0, 5)
                .WithTaggedFlag("FSPOL", 5)
                .WithTaggedFlag("FSEDGE", 6)
                .WithReservedBits(7, 1)
                .WithTag("FSOFFSET", 8, 4)
                .WithReservedBits(12, 20)
                ;

            Registers.IOModuleDebug.Define(this)
                .WithTaggedFlag("DBGEN", 0)
                .WithTaggedFlag("IOCLKON", 1)
                .WithTaggedFlag("APBCLKON", 2)
                .WithTag("DBGDATA", 3, 29)
                ;
        }

        private void IncomingFifoCountChangeAction(Fifo fifo, uint currentCount, uint previousCount)
        {
            // Ignore change in count if it didn't decrease
            if(currentCount >= previousCount)
            {
                return;
            }

            // Receive more data if there's a space to receive and Read command awaits.
            if(!fifo.Full && activeTransactionCommand.Value == Commands.Read && activeTransactionSizeLeft.Value > 0)
            {
                if(fifo.TryReceiveAndPush(ReceiveData))
                {
                    this.Log(LogLevel.Noisy, "Unfinished read command found and incoming FIFO has a space; receiving...");
                }
            }
            UpdateFifoThresholdInterruptStatus();
        }

        private void InterruptStatusSet(IoMasterInterrupts interrupt, bool value = true)
        {
            ioMasterInterruptsStatusFlags[(int)interrupt].Value = value;
            UpdateIRQ();
        }

        private void PerformDma()
        {
            var count = (int)(dmaTotCount.Value & 0xFFF);
            if (count == 0) return;

            var addr = dmaTargAddr.Value; // ulong

            // DMADIR: 0 = M2P (memory to peripheral), 1 = P2M (peripheral to memory)
            if (dmaDir.Value == 0)
            {
                // Memory -> outgoing FIFO: read from system memory, push bytes into TX path
                for (int i = 0; i < count; i++)
                {
                    var b = sysbusCtrl.ReadByte(addr + (uint)i, context: this);
                    if (!outgoingFifo.TryPush(b))
                    {
                        dmaErr = true;
                        break;
                    }
                }
            }
            else
            {
                // Incoming FIFO -> Memory: pop bytes from RX path, write to system memory
                for (int i = 0; i < count; i++)
                {
                    if (!incomingFifo.TryPop(out var b))
                    {
                        dmaErr = true;
                        break;
                    }
                    sysbusCtrl.WriteByte(addr + (uint)i, (byte)b, context: this);
                }
            }

            if (dmaErr)
            {
                InterruptStatusSet(IoMasterInterrupts.DmaError);
            }
            else
            {
                dmaCpl = true;
                InterruptStatusSet(IoMasterInterrupts.DmaComplete);
            }

            // Auto-clear DMAEN after completion (one-shot transfer)
            dmaEn.Value = 0;
        }

        // True when the CQ is enabled but must be held: either at an index match
        // (CQCURIDX == CQENDIDX, TRM 14.5.4) or with all of the pause-enable flags asserted.
        private bool IsCqPaused()
        {
            if(!cqEnabled.Value)
            {
                return false;
            }

            var curIdx = (uint)cqCurIdxField.Value & 0xFFu;
            var endIdx = (uint)cqEndIdxField.Value & 0xFFu;
            if(curIdx == endIdx)
            {
                return true;
            }

            var pauseMask = (uint)(cqPauseEnField.Value & 0xFFFFu);
            if(pauseMask != 0 && (cqSwFlags & pauseMask) == pauseMask)
            {
                return true;
            }

            return false;
        }

        // Re-evaluate the CQ after a flag / pause-enable / index register write: resume
        // processing when enabled and not paused, otherwise surface the paused condition.
        private void CheckCqPauseAndProcess()
        {
            if(!cqEnabled.Value)
            {
                return;
            }

            if(IsCqPaused())
            {
                InterruptStatusSet(IoMasterInterrupts.CommandQueuePaused);
                return;
            }

            ProcessCommandQueue();
        }

        // Walk the command queue in memory. Each entry is a pair of 32-bit words:
        // (target register address, data). For every pair we execute an internal write to this
        // peripheral -- which is how queued operations such as DMACFG/DMAEN kick off a transfer
        // (TRM 14.5.1). CQCURIDX advances once per completed DMA operation and the queue pauses
        // when it reaches CQENDIDX (TRM 14.5.4); an entry whose address has bit 0 set raises the
        // CommandQueueUPD interrupt for that write (TRM 14.5.2).
        private void ProcessCommandQueue()
        {
            if(!cqEnabled.Value || cqProcessing)
            {
                return;
            }

            cqProcessing = true;
            cqTip = true;   // transfer in progress (remains active even while paused, TRM 14.5.2)
            try
            {
                var entriesProcessed = 0;
                while(cqEnabled.Value && !IsCqPaused() && !cqErr)
                {
                    if(++entriesProcessed > CqMaxEntriesPerRun)
                    {
                        this.Log(LogLevel.Warning, "Command queue hit the safety limit ({0} entries) without reaching its end index; stopping to avoid a runaway walk.", CqMaxEntriesPerRun);
                        cqErr = true;
                        InterruptStatusSet(IoMasterInterrupts.CommandQueueError);
                        break;
                    }

                    uint regAddressWord, dataWord;
                    try
                    {
                        regAddressWord = sysbusCtrl.ReadDoubleWord((ulong)cqAddress, context: this);
                        dataWord = sysbusCtrl.ReadDoubleWord((ulong)(cqAddress + 4), context: this);
                    }
                    catch(Exception ex)
                    {
                        this.Log(LogLevel.Error, "Command queue fetch from 0x{0:X} failed ({1}); aborting.", cqAddress, ex.Message);
                        cqErr = true;
                        InterruptStatusSet(IoMasterInterrupts.CommandQueueError);
                        break;
                    }

                    // Advance the live CQADDR pointer to the next pair before executing this one.
                    cqAddress += 8;
                    try
                    {
                        cqAddressField.Value = (uint)(cqAddress >> 2);   // field holds bits [28:2] of the byte address
                    }
                    catch(Exception)
                    {
                        // The live pointer is informational only; ignore if it cannot be updated.
                    }

                    var interruptOnThisEntry = (regAddressWord & 1u) != 0;   // TRM 14.5.2 bit-0 trick
                    var regAddress = regAddressWord & ~1UL;                  // strip the flag bit -> real register address
                    var isDmaOperation = ((regAddress & 0xFFuL) == (ulong)(Registers.DmaConfiguration)) && (dataWord & 1u) != 0;

                    if(regAddress != 0)
                    {
                        sysbusCtrl.WriteDoubleWord(regAddress, dataWord, context: this);   // execute the queued register write
                    }

                    if(interruptOnThisEntry)
                    {
                        InterruptStatusSet(IoMasterInterrupts.CommandQueueUPD);
                    }

                    if(isDmaOperation)
                    {
                        cqCurIdxField.Value = (uint)((cqCurIdxField.Value + 1) & 0xFFu);   // one operation completed
                        if(IsCqPaused())
                        {
                            InterruptStatusSet(IoMasterInterrupts.CommandQueuePaused);
                            break;
                        }
                    }
                }

                // Clear the active indicator only when no longer enabled or on error -- per TRM 14.5.2
                // CQTIP stays set while merely paused waiting for software to post more operations.
                if(!cqEnabled.Value || cqErr)
                {
                    cqTip = false;
                }
            }
            finally
            {
                cqProcessing = false;
            }
        }

        private bool IsTransactionValid(Commands command, uint size, uint offsetCount, int spiSlaveSelect, out string errorMessage)
        {
            errorMessage = null;
            if(command != Commands.Read && command != Commands.Write)
            {
                errorMessage = $"Unsupported transaction command: {command}";
            }
            else if(command == Commands.Read && size == 0)
            {
                errorMessage = "Read transaction with size 0 is illegal.";
            }
            // NOTE: a Write with an empty outgoing FIFO is NOT rejected here. The full-duplex
            // firmware path writes CMD (transaction start) before pushing TX data via FIFOPUSH;
            // the deferred OutgoingFifoCountChangeAction sends each word as it lands in the FIFO.
            else if(offsetCount > 5)
            {
                errorMessage = $"Invalid transaction offset count: {offsetCount}";
            }
            else if(spiMasterEnabled.Value && !spiPeripherals.ContainsKey(spiSlaveSelect))
            {
                errorMessage = $"Transaction cannot be completed. There's no SPI peripheral registered with ID: {spiSlaveSelect}";
            }
            else if(i2cMasterEnabled.Value && !i2cPeripherals.ContainsKey((int)i2cSlaveAddress))
            {
                errorMessage = $"Transaction cannot be completed. There's no I2C peripheral registered with ID: {i2cSlaveAddress}";
            }
            return errorMessage == null;
        }

        private void OutgoingFifoCountChangeAction(Fifo fifo, uint currentCount, uint previousCount)
        {
            // Ignore if we are currently sending data
            if(currentCount <= previousCount)
            {
                return;
            }

            // Send more data if there's data to send and Write command awaits.
            if(!fifo.Empty && activeTransactionCommand.Value == Commands.Write && activeTransactionSizeLeft.Value > 0)
            {
                if(fifo.TryPop(out var value))
                {
                    this.Log(LogLevel.Noisy, "Unfinished write command found and outgoing FIFO contains data; sending 0x{0:X}...", value);
                    if(IsFullDuplex())
                    {
                        SendDataFullDuplex(value);
                    }
                    else
                    {
                        SendData(value);
                    }
                }
            }
            UpdateFifoThresholdInterruptStatus();
        }

        private uint ReceiveData()
        {
            if(activeTransactionSizeLeft.Value > 0)
            {
                var bytesToReceive = Math.Min(4, (uint)activeTransactionSizeLeft.Value);
                uint result = 0;
                if(ActiveTransactionPeripheral is ISPIPeripheral spiPeripheral)
                {
                    for(int i = 0; i < bytesToReceive; i++)
                    {
                        BitHelper.UpdateWithShifted(ref result, spiPeripheral.Transmit(0), (int)(i * 8), 8);
                    }
                }
                else if(ActiveTransactionPeripheral is II2CPeripheral i2cPeripheral)
                {
                    var data = i2cPeripheral.Read((int)bytesToReceive);
                    foreach(var item in data.Select((value, index) => new { index, value }))
                    {
                        BitHelper.UpdateWithShifted(ref result, item.value, (int)(item.index * 8), 8);
                    }
                }
                else
                {
                    // This code should be unreachable
                    throw new ArgumentException("Peripheral has to be selected before receiving data!");
                }
                activeTransactionSizeLeft.Value -= bytesToReceive;
                TryFinishTransaction();
                return result;
            }
            else
            {
                throw new ArgumentException($"Data shouldn't be received if activeTransactionSizeLeft equals 0!");
            }
        }

        private void Send(uint data, uint size, bool forceMSBFirst = false)
        {
            if(ActiveTransactionPeripheral is ISPIPeripheral spiPeripheral)
            {
                var dataBytes = BitHelper.GetBytesFromValue(data, (int)size, reverse: !forceMSBFirst);
                foreach(var dataByte in dataBytes)
                {
                    spiPeripheral.Transmit(dataByte);
                    this.Log(LogLevel.Noisy, "Byte sent to the SPI peripheral: 0x{0:X}", dataByte);
                }
            }
            else if(ActiveTransactionPeripheral is II2CPeripheral i2cPeripheral)
            {
                var dataBytes = BitHelper.GetBytesFromValue(data, (int)size, reverse: !forceMSBFirst);
                i2cPeripheral.Write(dataBytes);
                this.Log(LogLevel.Noisy, "{0} byte(s) sent to the I2C peripheral: 0x{0:X08}", size, data);
            }
            else
            {
                // This code should be unreachable
                throw new ArgumentException("Peripheral has to be selected before sending data!");
            }
        }

        private void SendData(uint value)
        {
            if(activeTransactionSizeLeft.Value > 0)
            {
                var bytesToSend = Math.Min(4, (uint)activeTransactionSizeLeft.Value);
                Send(value, bytesToSend);
                activeTransactionSizeLeft.Value -= bytesToSend;
                TryFinishTransaction();
            }
            else
            {
                throw new ArgumentException("Data shouldn't be sent if activeTransactionSizeLeft equals 0!");
            }
        }

        // Full-duplex variant of SendData: for each byte sent to the slave, also capture the
        // slave's MISO output and pack it (little-endian, like ReceiveData) into a double word
        // that is pushed into the incoming FIFO so firmware can read the response back.
        private void SendDataFullDuplex(uint value)
        {
            if(activeTransactionSizeLeft.Value > 0)
            {
                var bytesToSend = Math.Min(4, (uint)activeTransactionSizeLeft.Value);
                if(ActiveTransactionPeripheral is ISPIPeripheral spiPeripheral)
                {
                    var dataBytes = BitHelper.GetBytesFromValue(value, (int)bytesToSend, reverse: true);
                    uint misoResult = 0;
                    for(int i = 0; i < bytesToSend; i++)
                    {
                        var inByte = spiPeripheral.Transmit(dataBytes[i]);
                        BitHelper.UpdateWithShifted(ref misoResult, (uint)inByte, (int)(i * 8), 8);
                        this.Log(LogLevel.Noisy, "Full-duplex: sent 0x{0:X2}, received MISO 0x{1:X2}", dataBytes[i], inByte);
                    }

                    if(!incomingFifo.TryPush(misoResult))
                    {
                        this.Log(LogLevel.Warning, "Cannot push full-duplex MISO data to the incoming FIFO; dropping 0x{0:X8}", misoResult);
                    }
                }

                activeTransactionSizeLeft.Value -= bytesToSend;
                TryFinishTransaction();
            }
            else
            {
                throw new ArgumentException("Data shouldn't be sent if activeTransactionSizeLeft equals 0!");
            }
        }

        private void SendTransactionOffset(uint count)
        {
            // Depending on the offset count:
            // * for high=0xDEADBEEF, low=0x12: count=1 sends only 0x12, count=2: 0xEF12...
            // * transfer always begins with the MSB of the resulting value (0xEF for the count=2 example).
            if(count > 0)
            {
                if(count > 1)
                {
                    Send((uint)transactionOffsetHigh.Value, count - 1, forceMSBFirst: true);
                }
                Send((uint)transactionOffsetLow.Value, 1);
            }
        }

        private bool TryFinishTransaction()
        {
            if(activeTransactionSizeLeft.Value == 0)
            {
                this.Log(LogLevel.Noisy, "Command completed: {0}", activeTransactionCommand.Value);
                if(activeTransactionContinue)
                {
                    this.Log(LogLevel.Noisy, "The transmission won't be finished; the CONT flag was sent with the command.");
                }
                else
                {
                    if(ActiveTransactionPeripheral is ISPIPeripheral spiPeripheral)
                    {
                        spiPeripheral.FinishTransmission();
                    }
                    else if(ActiveTransactionPeripheral is II2CPeripheral i2cPeripheral)
                    {
                        i2cPeripheral.FinishTransmission();
                    }
                    else
                    {
                        // This code should be unreachable
                        throw new ArgumentException("Trying to finish transaction for which peripherial wasn't chosen");
                    }
                }
                activeTransactionCommand.Value = Commands.None;

                // Not sure if these are valid for the continuous transmission.
                activeTransactionStatus.Value = Status.Idle;
                status = Status.Idle;
                InterruptStatusSet(IoMasterInterrupts.CommandComplete);

                return true;
            }
            return false;
        }

        private void UpdateFifoThresholdInterruptStatus()
        {
            InterruptStatusSet(IoMasterInterrupts.FifoThreshold, value:
                outgoingFifo.BytesCount < fifoInterruptWriteThreshold.Value
                || incomingFifo.BytesCount > fifoInterruptReadThreshold.Value
            );
        }

        private void UpdateIRQ()
        {
            var newIrqState = false;
            for(var i = 0; i < IoMasterInterruptsCount; i++)
            {
                if(ioMasterInterruptsEnableFlags[i].Value && ioMasterInterruptsStatusFlags[i].Value)
                {
                    newIrqState = true;
                    break;
                }
            }

            if(newIrqState != IRQ.IsSet)
            {
                this.Log(LogLevel.Debug, "{0} IRQ", newIrqState ? "Setting" : "Resetting");
                IRQ.Set(newIrqState);
            }
        }

        private void Register<T>(Dictionary<int, T> container, T peripheral, TypedNumberRegistrationPoint<int> registrationPoint) where T : IPeripheral
        {
            if(container.ContainsKey(registrationPoint.Address))
            {
                throw new RegistrationException("The specified registration point is already in use.");
            }
            container.Add(registrationPoint.Address, peripheral);
            machine.RegisterAsAChildOf(this, peripheral, registrationPoint);
        }

        private void Unregister<T>(Dictionary<int, T> container, T peripheral) where T : IPeripheral
        {
            var toRemove = container.Where(x => x.Value.Equals(peripheral)).Select(x => x.Key).ToList(); //ToList required, as we remove from the source
            if(toRemove.Count == 0)
            {
                throw new RegistrationException("The specified peripheral was never registered.");
            }
            foreach(var key in toRemove)
            {
                container.Remove(key);
            }
            machine.UnregisterAsAChildOf(this, peripheral);
        }

        private string PrettyPendingPeripheral
        {
            get
            {
                if(spiMasterEnabled.Value)
                {
                    return $"SPI#{spiSlaveSelect.Value}";
                }
                else if(i2cMasterEnabled.Value)
                {
                    return $"I2C#{i2cSlaveAddress}";
                }
                return String.Empty;
            }
        }

        private string PrettyActivePeripheral
        {
            get
            {
                if(spiMasterEnabled.Value)
                {
                    return $"SPI#{activeSpiSlaveSelect}";
                }
                else if(i2cMasterEnabled.Value)
                {
                    return $"I2C#{i2cSlaveAddress}";
                }
                return String.Empty;
            }
        }

        private IPeripheral ActiveTransactionPeripheral
        {
            get
            {
                if(activeTransactionCommand.Value == Commands.None)
                {
                    return null;
                }
                else if(spiMasterEnabled.Value)
                {
                    return spiPeripherals[activeSpiSlaveSelect];
                }
                else if(i2cMasterEnabled.Value)
                {
                    return i2cPeripherals[(int)i2cSlaveAddress];
                }
                return null;
            }
        }

        private IEnumRegisterField<Commands> activeTransactionCommand;
        private IValueRegisterField activeTransactionSizeLeft;
        private IEnumRegisterField<Status> activeTransactionStatus;
        private IValueRegisterField fifoInterruptReadThreshold;
        private IValueRegisterField fifoInterruptWriteThreshold;
        private IFlagRegisterField[] ioMasterInterruptsEnableFlags;
        private IFlagRegisterField[] ioMasterInterruptsStatusFlags;
        private IFlagRegisterField spiMasterEnabled;
        private IFlagRegisterField i2cMasterEnabled;
        private IEnumRegisterField<Commands> transactionCommand;
        private IFlagRegisterField transactionContinue;
        private IValueRegisterField transactionOffsetCount;
        private IValueRegisterField transactionOffsetHigh;
        private IValueRegisterField transactionOffsetLow;
        private IValueRegisterField transactionSize;
        private IValueRegisterField spiSlaveSelect;
        private IFlagRegisterField writePopToAdvanceReadPointer;
        private IFlagRegisterField i2cExtendedAdressingMode;

        private bool activeTransactionContinue;
        private int activeSpiSlaveSelect;
        private uint i2cSlaveAddress;
        private Status status;

        private readonly bool fullDuplex;

        // FULLDUP bit (SpiModuleMasterConfiguration 0x280, bit 2): when set by firmware, Write
        // transactions capture the slave's MISO output. Read from the register so it tracks config.
        private IFlagRegisterField fullDuplexReg;
        private bool IsFullDuplex() => fullDuplex || (fullDuplexReg != null && fullDuplexReg.Value);

        /*
            Both FIFOs occupy a single 64-byte memory:
            * 0x00 -- 0x1F outgoingFifo: "FIFO 0 (written by MCU, read by interface)",
            * 0x20 -- 0x3F incomingFifo: "FIFO 1 (written by interface, read by MCU)"
            Queues aren't used because random access is also needed.
        */
        private readonly Fifo incomingFifo;
        private readonly Fifo outgoingFifo;
        private readonly Dictionary<int, ISPIPeripheral> spiPeripherals;
        private readonly Dictionary<int, II2CPeripheral> i2cPeripherals;
        private readonly IMachine machine;
        private readonly IBusController sysbusCtrl;

        // DMA state
        private IValueRegisterField dmaEn, dmaDir;
        private IValueRegisterField dmaTotCount, dmaTargAddr;
        private volatile bool dmaTip = false, dmaCpl = false, dmaErr = false;

        // Command Queue (MSPI/IOM CQ) state -- see TRM 14.5 "Command Queueing"
        private IFlagRegisterField cqEnabled;
        private IValueRegisterField cqAddressField;
        private IValueRegisterField cqFlagsField;
        private IValueRegisterField cqIrqMaskField;
        private IValueRegisterField cqPauseEnField;
        private IValueRegisterField cqCurIdxField;
        private IValueRegisterField cqEndIdxField;

        // Live byte address of the next (address, data) pair to fetch from memory.
        private uint cqAddress;
        // Software-writable flag bits [7:0] backing CQFLAGS / CQFSET / CQFTGL / CQFCLR.
        private uint cqSwFlags;
        // Read-only status flags surfaced via CommandQueueStatus (CQTIP / CQERR).
        private volatile bool cqTip = false, cqErr = false;
        // Re-entrancy guard so a queued write cannot re-enter the walker mid-run.
        private bool cqProcessing;

        // Upper bound on pairs processed in one synchronous run; guards against a runaway walk if
        // firmware misconfigures the indices or points CQADDR at garbage (TRM 14.5.2 CQERR case).
        private const int CqMaxEntriesPerRun = 1024;

        private const int IoMasterInterruptsCount = 15;
        private const int MaxSpiPeripheralsConnected = 4;

        private class Fifo
        {
            public Fifo(string name, IPeripheral owner)
            {
                this.name = name;
                this.owner = owner;
                Reset();
            }

            public uint DirectGet(uint index)
            {
                if(CheckResetFlag("get"))
                {
                    return memory[index];
                }
                return 0x0;
            }

            public void DirectSet(uint index, uint value)
            {
                if(CheckResetFlag("set"))
                {
                    memory[index] = value;
                }
            }

            public void Reset()
            {
                SoftwareReset();
                resetFlag = false;
            }

            public override string ToString()
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendFormat("{0}: ", name);
                builder.AppendFormat("Count: {0}; values: ", Count, headIndex, tailIndex);
                for(int index = headIndex; index < headIndex + Count; index++)
                {
                    builder.AppendFormat("0x{0:X8} ", memory[index % DoubleWordCapacity]);
                }
                return builder.ToString();
            }

            public bool TryAdvancePointer()
            {
                if(CheckResetFlag("advance the pointer") && Check(!Empty, "Cannot advance the pointer; FIFO is empty."))
                {
                    // Clear the current head. It won't be read with Peek/Pop without a prior
                    // setting it with Push but it could be read with a random access.
                    memory[headIndex] = 0x0;

                    headIndex = (headIndex + 1) % DoubleWordCapacity;
                    Count--;
                    return true;
                }
                return false;
            }

            public bool TryPeek(out uint value, string actionName = "peek")
            {
                if(CheckResetFlag(actionName) && Check(!Empty, $"Cannot {actionName}; FIFO is empty."))
                {
                    value = memory[headIndex];
                    return true;
                }
                value = 0x0;
                return false;
            }

            public bool TryPop(out uint value)
            {
                if(TryPeek(out value, actionName: "pop"))
                {
                    TryAdvancePointer();
                    return true;
                }
                return false;
            }

            public bool TryPush(uint value)
            {
                return TryReceiveAndPush(() => value);
            }

            // To prevent losing the received value, the receiveFunction will only be called if push is possible.
            public bool TryReceiveAndPush(Func<uint> receiveFunction)
            {
                if(CheckResetFlag("push") && Check(!Full, "Cannot push; FIFO is full."))
                {
                    Push(receiveFunction());
                    return true;
                }
                return false;
            }

            public Action<Fifo, uint, uint> CountChangeAction { get; set; }

            public uint BytesCapacity => DoubleWordCapacity * 4;

            public uint BytesCount => Count * 4;

            public uint BytesLeft => (DoubleWordCapacity * 4) - BytesCount;

            public bool Empty => Count == 0;

            public bool Full => Count == DoubleWordCapacity;

            public uint Pointer => (uint)headIndex;

            public bool ResetFlag
            {
                get => resetFlag;
                set
                {
                    if(!resetFlag && value)
                    {
                        SoftwareReset();
                    }
                    resetFlag = value;
                }
            }

            private bool Check(bool condition, string errorMessage)
            {
                if(!condition)
                {
                    owner.DebugLog("{0}: {1}", name, errorMessage);
                    return false;
                }
                return true;
            }

            private bool CheckResetFlag(string actionName)
            {
                return Check(!resetFlag, $"Cannot {actionName}; the reset flag is set.");
            }

            private void ClearMemory()
            {
                for(int i = 0; i < DoubleWordCapacity; i++)
                {
                    memory[i] = 0x0;
                }
            }

            private void Push(uint value)
            {
                if(Full)
                {
                    throw new ArgumentException($"Cannot push; the {name} is full!");
                }
                tailIndex = (tailIndex + 1) % DoubleWordCapacity;
                Count++;
                memory[tailIndex] = value;
            }

            private void SoftwareReset()
            {
                ClearMemory();
                Count = 0;
                headIndex = 0;
                tailIndex = -1;
            }

            private uint Count
            {
                get => count;
                set
                {
                    if(value < 0 || value > DoubleWordCapacity)
                    {
                        throw new ArgumentException($"{name}: Invalid value for Count: {value} (Capacity: {DoubleWordCapacity})");
                    }
                    var previousCount = count;
                    count = value;
                    CountChangeAction?.Invoke(this, count, previousCount);
                }
            }

            private uint count;
            private int headIndex;
            private bool resetFlag;
            private int tailIndex = -1;

            private readonly uint[] memory = new uint[DoubleWordCapacity];
            private readonly string name;
            private readonly IPeripheral owner;

            private const int DoubleWordCapacity = 8;
        }

        private enum Commands
        {
            None = 0x0,
            Write = 0x1,
            Read = 0x2,
            TestModeWrite = 0x3,
            TestModeRead = 0x4,
        }

        private enum Status
        {
            Error = 0x1,
            Active = 0x2,
            Idle = 0x4,
            Wait = 0x6,
        }

        private enum IoMasterInterrupts
        {
            CommandComplete,
            FifoThreshold,
            ReadFifoUnderflow,
            WriteFifoOverflow,
            I2CNak,
            IllegalFifoAccess,
            IllegalCommand,
            StartCommand,
            StopCommand,
            ArbitrationLoss,
            DmaComplete,
            DmaError,
            CommandQueuePaused,
            // No sure what "UPD" means here. This is the full description:
            // "CQ write operation performed a register write with the register address bit 0 set to 1.
            // The low address bits in the CQ address fields are unused and bit 0 can be used to trigger
            // an interrupt to indicate when this register write is performed by the CQ operation."
            CommandQueueUPD,
            CommandQueueError,
        }

        private enum Registers : long
        {
            OutgoingFifoAccessPort = 0x0,
            IncomingFifoAccessPort = 0x20,
            FifoSizeAndRemainingSlotsOpenValues = 0x100,
            FifoThresholdConfiguration = 0x104,
            FifoPop = 0x108,
            FifoPush = 0x10C,
            FifoControl = 0x110,
            FifoPointers = 0x114,
            IOClockConfiguration = 0x118,
            SubmoduleControl = 0x11C,
            CommandAndOffset = 0x120,
            DcxControlAndCeUsageSelection = 0x124,
            HighOrderBytesBfOffsetForIOTransaction = 0x128,
            CommandStatus = 0x12C,
            IOMasterInterruptsEnable = 0x200,
            IOMasterInterruptsStatus = 0x204,
            IOMasterInterruptsClear = 0x208,
            IOMasterInterruptsSet = 0x20C,
            DmaTriggerEnable = 0x210,
            DmaTriggerStatus = 0x214,
            DmaConfiguration = 0x218,
            DmaTotalTransferCount = 0x21C,
            DmaTargetAddress = 0x220,
            DmaStatus = 0x224,
            CommandQueueConfiguration = 0x228,
            CommandQueueTargetReadAddress = 0x22C,
            CommandQueueStatus = 0x230,
            CommandQueueFlag = 0x234,
            CommandQueueFlagSetClear = 0x238,
            CommandQueuePauseEnable = 0x23C,
            CommandQueueCurrentIndexValue = 0x240,
            CommandQueueEndIndexValue = 0x244,
            IOModuleStatus = 0x248,
            SpiModuleMasterConfiguration = 0x280,
            I2CMasterConfiguration = 0x2C0,
            I2CDeviceConfiguration = 0x2C4,
            I2SControl = 0x300,
            I2SClockControl = 0x304,
            I2SFrameSyncControl = 0x308,
            IOModuleDebug = 0x388,
        }

        private enum SubmoduleTypes : uint
        {
            SpiMaster = 0x0,
            I2CMaster = 0x1,
            SpiSlave = 0x2,
            I2CSlave = 0x3,
            I2SMaster_Slave = 0x4,
            NotInstalled = 0x7,
        }
    }
}