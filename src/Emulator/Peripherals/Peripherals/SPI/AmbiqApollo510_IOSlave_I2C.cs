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
using Antmicro.Renode.Peripherals.I2C;
using Antmicro.Renode.Peripherals.SPI;
using Antmicro.Renode.Utilities;

namespace Antmicro.Renode.Peripherals.SPI
{
    public class AmbiqApollo510_IOSlave_I2C : II2CPeripheral, IPeripheral
    {
        private readonly AmbiqApollo510_IOSlave impl;

        public AmbiqApollo510_IOSlave_I2C()
        {
            impl = new AmbiqApollo510_IOSlave();
        }

        public void Reset()
        {
            impl.Reset();
        }

        public void Write(byte[] data)
        {
            impl.Write_I2C(data);
        }

        public byte[] Read(int count = 1)
        {
            return impl.Read_I2C(count);
        }

        void II2CPeripheral.FinishTransmission()
        {
            impl.FinishTransmission_I2C();
        }
    }
}