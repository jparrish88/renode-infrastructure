//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;

using Antmicro.Renode.Core;

namespace Antmicro.Renode.Peripherals.UART
{
    public class AmbiqApollo510_UART : PL011
    {
        public AmbiqApollo510_UART(IMachine machine) : base(machine, ambiqDma: true)
        {
        }
    }
}
