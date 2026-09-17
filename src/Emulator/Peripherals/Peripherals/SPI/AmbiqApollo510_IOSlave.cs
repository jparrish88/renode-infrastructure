//
// Copyright (c) 2010-2025 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
using System;
using System.Collections.Generic;
using System.Linq;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class AmbiqApollo510_IOSlave : ISPIPeripheral, IPeripheral
    {
        public AmbiqApollo510_IOSlave()
        {
            lram = new byte[256];
            fifoBuffer = new Queue<byte>();
            sramBuffer = new Queue<byte>();
            Reset();
        }

        public void Reset()
        {
            Array.Clear(lram, 0, lram.Length);
            fifoBuffer.Clear();
            sramBuffer.Clear();
            
            fifoPtr = 0;
            fifoSize = 0;
            fifoCfg = 0x20000000;
            fifoThr = 0;
            fupd = 0;
            fifoCtr = 0;
            fifoInc = 0;
            cfg = 0x1;
            prenc = 0;
            ioIntCtl = 0;
            genAdd = 0;
            addPtr = 0;
            dmaCfg = 0;
            dmaTotCount = 0;
            dmaTargAddr = 0;
            dmaStat = 0;
            intEn = 0;
            intStat = 0;
            regAccIntEn = 0;
            regAccIntStat = 0;
            
            UpdateFifoArea();
        }

        private void UpdateFifoArea()
        {
            roBase = (int)(((fifoCfg >> 24) & 0x3F) * 8);
            fifoBase = (int)((fifoCfg & 0x1F) * 8);
            fifoMax = (int)(((fifoCfg >> 8) & 0x3F) * 8);
            if(fifoMax == 0) fifoMax = 256;
        }

        // ISPIPeripheral
        public byte Transmit(byte data)
        {
            this.Log(LogLevel.Noisy, "IOSlave SPI transmit: 0x{0:X}", data);
            
            if(addressPhase)
            {
                addressPhase = false;
                addPtr = data;
                this.Log(LogLevel.Debug, "IOSlave address set to 0x{0:X}", addPtr);
                
                if(addPtr >= 0x78 && addPtr <= 0x7F)
                {
                    registerAccess = true;
                    return 0;
                }
                return 0;
            }
            
            if(registerAccess)
            {
                return HandleRegisterAccess(data);
            }
            
            return HandleLramAccess(data);
        }

        private byte HandleRegisterAccess(byte data)
        {
            uint regAddr = (uint)addPtr;
            this.Log(LogLevel.Debug, "IOSlave register access: reg=0x{0:X}, data=0x{1:X}", regAddr, data);
            
            switch(regAddr)
            {
                case 0x78: return (byte)(intEn & 0xFF);
                case 0x79: return (byte)(intStat & 0xFF);
                case 0x7A:
                    intStat = 0;
                    intEn = 0;
                    return 0;
                case 0x7B:
                    intStat |= (uint)(data & 0xFF);
                    intEn |= (uint)(data & 0xFF);
                    return 0;
                case 0x7C: return (byte)(fifoCtr & 0xFF);
                case 0x7D: return (byte)((fifoCtr >> 8) & 0xFF);
                case 0x7F:
                    if(fifoBuffer.Count > 0)
                    {
                        byte val = fifoBuffer.Dequeue();
                        fifoCtr = (uint)Math.Max(0, fifoCtr - 1);
                        return val;
                    }
                    return 0;
                default:
                    if(regAddr < 0x78 && regAddr < lram.Length)
                    {
                        byte oldVal = lram[regAddr];
                        lram[regAddr] = data;
                        return oldVal;
                    }
                    return 0;
            }
        }

        private byte HandleLramAccess(byte data)
        {
            if(addPtr >= lram.Length)
            {
                if((cfg & (1 << 20)) != 0)
                {
                    addPtr = (byte)fifoBase;
                }
                else
                {
                    this.Log(LogLevel.Warning, "IOSlave address pointer out of bounds: 0x{0:X}", addPtr);
                    addPtr = 0;
                }
            }
            
            byte oldVal = lram[addPtr];
            lram[addPtr] = data;
            addPtr = (byte)(addPtr + 1);
            
            if(addPtr >= fifoBase && addPtr < fifoMax && (fifoInc > 0 || oldVal != data))
            {
                fifoBuffer.Enqueue(data);
                fifoCtr = Math.Min(fifoCtr + fifoInc, 1023);
                UpdateFifoInterrupts();
            }
            
            return oldVal;
        }

        private void UpdateFifoInterrupts()
        {
            if(fifoCtr <= fifoThr)
            {
                intStat |= 1;
            }
            if(fifoCtr > 64)
            {
                intStat |= (1 << 1);
            }
        }

        void ISPIPeripheral.FinishTransmission()
        {
            this.Log(LogLevel.Noisy, "IOSlave SPI finish transmission");
            addressPhase = true;
            registerAccess = false;
        }

        // Public method for I2C wrapper
        public void FinishTransmission_I2C()
        {
            addressPhase = true;
            registerAccess = false;
        }

        // Public I2C methods for I2C wrapper
        public void Write_I2C(byte[] data)
        {
            if(data == null || data.Length == 0) return;
            
            this.Log(LogLevel.Noisy, "IOSlave I2C write: {0} bytes", data.Length);
            
            if(data.Length == 1)
            {
                addPtr = data[0];
                this.Log(LogLevel.Debug, "IOSlave I2C address set to 0x{0:X}", addPtr);
            }
            else
            {
                foreach(byte b in data.Skip(1))
                {
                    if(addPtr >= 0x78 && addPtr <= 0x7F)
                    {
                        HandleRegisterAccess(b);
                    }
                    else
                    {
                        HandleLramAccess(b);
                    }
                }
            }
        }

        public byte[] Read_I2C(int count = 1)
        {
            byte[] result = new byte[count];
            
            if(addPtr >= 0x78 && addPtr <= 0x7F)
            {
                for(int i = 0; i < count; i++)
                {
                    uint regAddr = (uint)(addPtr + i);
                    if(regAddr == 0x78)
                        result[i] = (byte)(intEn & 0xFF);
                    else if(regAddr == 0x79)
                        result[i] = (byte)(intStat & 0xFF);
                    else if(regAddr == 0x7F)
                    {
                        if(fifoBuffer.Count > 0)
                        {
                            result[i] = fifoBuffer.Dequeue();
                            fifoCtr = (uint)Math.Max(0, fifoCtr - 1);
                        }
                        else
                        {
                            result[i] = 0;
                        }
                    }
                    else
                    {
                        result[i] = 0;
                    }
                }
            }
            else
            {
                for(int i = 0; i < count; i++)
                {
                    if(addPtr + i < lram.Length)
                    {
                        result[i] = lram[addPtr + i];
                    }
                    else
                    {
                        result[i] = 0;
                    }
                }
                addPtr = (byte)Math.Min(lram.Length - 1, addPtr + count);
            }
            
            this.Log(LogLevel.Noisy, "IOSlave I2C read: {0} bytes from 0x{1:X}", count, addPtr);
            return result;
        }

        // CPU memory-mapped access
        public uint ReadRegister(uint offset)
        {
            switch(offset)
            {
                case 0x100: return (uint)((fifoSize << 8) | fifoPtr);
                case 0x104: return fifoCfg;
                case 0x108: return fifoThr;
                case 0x10C: return fupd;
                case 0x110: return fifoCtr;
                case 0x114: return fifoInc;
                case 0x118: return cfg;
                case 0x11C: return prenc;
                case 0x120: return ioIntCtl;
                case 0x124: return genAdd;
                case 0x128: return addPtr;
                case 0x130: return dmaCfg;
                case 0x134: return dmaTotCount;
                case 0x138: return dmaTargAddr;
                case 0x13C: return dmaStat;
                case 0x200: return intEn;
                case 0x204: return intStat;
                case 0x208: return 0;
                case 0x20C: return 0;
                case 0x210: return regAccIntEn;
                case 0x214: return regAccIntStat;
                case 0x218: return 0;
                case 0x21C: return 0;
                default:
                    if(offset < 0x100 && offset < 256)
                        return lram[offset];
                    return 0;
            }
        }

        public void WriteRegister(uint offset, uint value)
        {
            switch(offset)
            {
                case 0x100:
                    fifoPtr = (byte)(value & 0xFF);
                    fifoSize = (byte)((value >> 8) & 0xFF);
                    break;
                case 0x104:
                    fifoCfg = value;
                    UpdateFifoArea();
                    break;
                case 0x108: fifoThr = (byte)(value & 0xFF); break;
                case 0x10C: fupd = value & 0x3; break;
                case 0x110: fifoCtr = value & 0x3FF; break;
                case 0x114: fifoInc = value & 0x3FF; break;
                case 0x118: cfg = value; break;
                case 0x11C: prenc = (byte)(value & 0x1F); break;
                case 0x120:
                    ioIntCtl = value;
                    if((value & (1 << 16)) != 0) { intStat = 0; intEn = 0; }
                    intEn |= (uint)((value >> 24) & 0xFF);
                    intStat |= (uint)((value >> 24) & 0xFF);
                    break;
                case 0x124: genAdd = (byte)(value & 0xFF); break;
                case 0x128: addPtr = (byte)(value & 0xFF); break;
                case 0x130: dmaCfg = value; break;
                case 0x134: dmaTotCount = value; break;
                case 0x138: dmaTargAddr = value; break;
                case 0x13C: dmaStat = value; break;
                case 0x200: intEn = value; break;
                case 0x204: intStat = value; break;
                case 0x208: intStat &= ~value; break;
                case 0x20C: intStat |= value; break;
                case 0x210: regAccIntEn = value; break;
                case 0x214: regAccIntStat = value; break;
                case 0x218: regAccIntStat &= ~value; break;
                case 0x21C: regAccIntStat |= value; break;
                default:
                    if(offset < 0x100 && offset < 256)
                        lram[offset] = (byte)value;
                    break;
            }
        }

        // Properties for register file access
        public byte FifoPtr { get => fifoPtr; set { fifoPtr = value; } }
        public byte FifoSize { get => fifoSize; set { fifoSize = value; } }
        public uint FifoCfg { get => fifoCfg; set { fifoCfg = value; UpdateFifoArea(); } }
        public byte FifoThr { get => fifoThr; set { fifoThr = value; } }
        public uint Fupd { get => fupd; set { fupd = value; } }
        public uint FifoCtr { get => fifoCtr; set { fifoCtr = value; } }
        public uint FifoInc { get => fifoInc; set { fifoInc = value; } }
        public uint Cfg { get => cfg; set { cfg = value; } }
        public byte PrenC { get => prenc; set { prenc = (byte)value; } }
        public uint IntEn { get => intEn; set { intEn = value; } }
        public uint IntStat { get => intStat; set { intStat = value; } }
        public byte GenAdd { get => genAdd; set { genAdd = (byte)value; } }
        public byte AddPtr { get => addPtr; set { addPtr = (byte)value; } }
        public uint DmaCfg { get => dmaCfg; set { dmaCfg = value; } }
        public uint DmaTotCount { get => dmaTotCount; set { dmaTotCount = value; } }
        public uint DmaTargAddr { get => dmaTargAddr; set { dmaTargAddr = value; } }
        public uint DmaStat { get => dmaStat; set { dmaStat = value; } }
        public uint RegAccIntEn { get => regAccIntEn; set { regAccIntEn = value; } }
        public uint RegAccIntStat { get => regAccIntStat; set { regAccIntStat = value; } }
        public byte ReadLRAM(int idx) => (idx < lram.Length) ? lram[idx] : (byte)0;
        public void WriteLRAM(int idx, byte val) { if(idx < lram.Length) lram[idx] = val; }

        private byte[] lram;
        private Queue<byte> fifoBuffer;
        private Queue<byte> sramBuffer;
        
        private bool addressPhase = true;
        private bool registerAccess = false;
        private byte addPtr = 0;
        
        private byte fifoPtr;
        private byte fifoSize;
        private uint fifoCfg;
        private byte fifoThr;
        private uint fupd;
        private uint fifoCtr;
        private uint fifoInc;
        private uint cfg;
        private byte prenc;
        private uint ioIntCtl;
        private byte genAdd;
        private uint dmaCfg;
        private uint dmaTotCount;
        private uint dmaTargAddr;
        private uint dmaStat;
        private uint intEn;
        private uint intStat;
        private uint regAccIntEn;
        private uint regAccIntStat;
        
        private int roBase;
        private int fifoBase;
        private int fifoMax;
    }
}