//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text can be found in 'licenses/MIT.txt'.
//

using System.Collections.Generic;

using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Peripherals.Memory;
using Antmicro.Renode.Peripherals.UART;
using Antmicro.Renode.Time;

using NUnit.Framework;

namespace Antmicro.Renode.PeripheralsTests
{
    [TestFixture]
    public class AmbiqApollo510_UARTTests
    {
        private const ulong BufferAddress = 0x2000_1000UL;

        [Test]
        public void SdkDmaSequenceTransfersDataAndReportsCompletion()
        {
            var machine = new Machine(createLocalTimeSource: true);
            var buffer = new MappedMemory(machine, 0x100);
            machine.SystemBus.Register(buffer, new BusRangeRegistration(BufferAddress, 0x100));

            var uart = new AmbiqApollo510_UART(machine);
            var transmitted = new List<byte>();
            uart.CharReceived += transmitted.Add;

            machine.SystemBus.WriteBytes(new byte[] { 0x41, 0x42 }, BufferAddress);

            // Match am_hal_uart_dma_configure(), then am_hal_uart_dma_transfer_start().
            uart.WriteDoubleWord(0x24, 13); // IBRD for the test UART clock.
            uart.WriteDoubleWord(0x28, 1); // FBRD for approximately 115200 baud.
            uart.WriteDoubleWord(0x30, 0x301); // UARTEN | TXE | RXE
            uart.WriteDoubleWord(0x38, 1u << 11); // DMACPL interrupt enable
            uart.WriteDoubleWord(0x44, 1u << 11); // clear stale completion
            uart.WriteDoubleWord(0x48, 0); // DCR: set priority/disable before COUNT
            uart.WriteDoubleWord(0x50, 2);
            uart.WriteDoubleWord(0x4C, (uint)BufferAddress);
            uart.WriteDoubleWord(0x48, 1u << 1); // TXDMAE rising edge starts the transfer

            // DMA completion is asynchronous with respect to the enabling
            // register write, matching the SDK's bDMABusy state transition.
            Assert.That(transmitted, Is.Empty);
            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.Zero);

            ((BaseClockSource)machine.ClockSource).Advance(TimeInterval.FromMilliseconds(1), immediately: true);

            Assert.That(transmitted, Is.EqualTo(new byte[] { 0x41, 0x42 }));
            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.EqualTo(1u << 11));
            Assert.That(uart.ReadDoubleWord(0x40), Is.EqualTo(1u << 11));
            Assert.That(uart.IRQ.IsSet, Is.True, "DMA-complete must assert the UART IRQ line");
            Assert.That(uart.ReadDoubleWord(0x50), Is.Zero);

            // The Apollo HAL clears RSR.DMACPL by writing zero to the status
            // field after servicing the interrupt, not by writing ICR.
            uart.WriteDoubleWord(0x48, 0);
            uart.WriteDoubleWord(0x04, 0);
            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.Zero);
            Assert.That(uart.IRQ.IsSet, Is.False, "clearing RSR.DMACPL must release the UART IRQ line");

            // Rewriting TXDMAE while it is already set must not launch another transfer.
            machine.SystemBus.WriteBytes(new byte[] { 0x43, 0x44 }, BufferAddress);
            uart.WriteDoubleWord(0x50, 2);
            uart.WriteDoubleWord(0x4C, (uint)BufferAddress);
            uart.WriteDoubleWord(0x48, 1u << 1);
            Assert.That(transmitted, Is.EqualTo(new byte[] { 0x41, 0x42 }));

            // The SDK clears the completion interrupt, disables DMA, and reuses the block.
            uart.WriteDoubleWord(0x44, 1u << 11);
            uart.WriteDoubleWord(0x48, 0);
            uart.WriteDoubleWord(0x50, 2);
            uart.WriteDoubleWord(0x4C, (uint)BufferAddress);
            uart.WriteDoubleWord(0x48, 1u << 1);

            ((BaseClockSource)machine.ClockSource).Advance(TimeInterval.FromMilliseconds(1), immediately: true);

            Assert.That(transmitted, Is.EqualTo(new byte[] { 0x41, 0x42, 0x43, 0x44 }));
            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.EqualTo(1u << 11));
            Assert.That(uart.IRQ.IsSet, Is.True, "re-armed DMA-complete must assert the UART IRQ line");
            Assert.That(uart.ReadDoubleWord(0x50), Is.Zero);

            // A third transaction verifies that the completion/clear/re-arm
            // sequence remains valid after repeated interrupt service.
            machine.SystemBus.WriteBytes(new byte[] { 0x45, 0x46 }, BufferAddress);
            uart.WriteDoubleWord(0x44, 1u << 11);
            uart.WriteDoubleWord(0x48, 0);
            uart.WriteDoubleWord(0x50, 2);
            uart.WriteDoubleWord(0x4C, (uint)BufferAddress);
            uart.WriteDoubleWord(0x48, 1u << 1);

            ((BaseClockSource)machine.ClockSource).Advance(TimeInterval.FromMilliseconds(1), immediately: true);

            Assert.That(transmitted, Is.EqualTo(new byte[] { 0x41, 0x42, 0x43, 0x44, 0x45, 0x46 }));
            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.EqualTo(1u << 11));
            Assert.That(uart.IRQ.IsSet, Is.True, "third DMA completion must assert the UART IRQ line");
            Assert.That(uart.ReadDoubleWord(0x50), Is.Zero);
        }

        [Test]
        public void ResetClearsDmaInterruptState()
        {
            var machine = new Machine(createLocalTimeSource: true);
            var buffer = new MappedMemory(machine, 0x100);
            machine.SystemBus.Register(buffer, new BusRangeRegistration(BufferAddress, 0x100));
            var uart = new AmbiqApollo510_UART(machine);

            machine.SystemBus.WriteBytes(new byte[] { 0x55 }, BufferAddress);
            uart.WriteDoubleWord(0x24, 13);
            uart.WriteDoubleWord(0x28, 1);
            uart.WriteDoubleWord(0x30, 0x301);
            uart.WriteDoubleWord(0x38, 1u << 11);
            uart.WriteDoubleWord(0x50, 1);
            uart.WriteDoubleWord(0x4C, (uint)BufferAddress);
            uart.WriteDoubleWord(0x48, 1u << 1);

            ((BaseClockSource)machine.ClockSource).Advance(TimeInterval.FromMilliseconds(1), immediately: true);

            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.EqualTo(1u << 11));

            uart.Reset();

            Assert.That(uart.ReadDoubleWord(0x3C) & (1u << 11), Is.Zero);
            Assert.That(uart.ReadDoubleWord(0x40), Is.Zero);
        }
    }
}
