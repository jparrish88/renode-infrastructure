//
// Copyright (c) 2010-2026 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//

using System;
using System.IO;

using Antmicro.Renode.Core;

namespace Antmicro.Renode.Peripherals.UART
{
    public class AmbiqApollo510_UART : PL011
    {
        private static int instanceCount = 0;

        public AmbiqApollo510_UART(IMachine machine) : base(machine, ambiqDma: true)
        {
            var idx = instanceCount++;
            var logPath = Path.Combine("/tmp/opencode", $"uart_{idx}.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                var writer = new StreamWriter(logPath, append: false);
                CharReceived += b =>
                {
                    writer.Write((char)b);
                    writer.Flush();
                };
            }
            catch (Exception)
            {
            }
        }
    }
}
