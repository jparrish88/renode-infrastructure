//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 MCU Miscellaneous Control Logic (MCUCTRL @ 0x40020000).
// Device-identification + power-on-default registers for an Apollo510B SiP
// (REV B.1), per the platform register CSV. Read-only model: firmware only
// reads these at boot; writes are ignored (silicon RAZ/WI behaviour).
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510_MCUCTRL : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510_MCUCTRL(IMachine machine) : base(machine)
        {
            DefineRegisters();
        }

        public long Size => 0x1000;

        private void DefineRegisters()
        {
            Registers.ChipPN.Define(this, 0x00).WithValueField(0, 32, name: "CHIPPN", valueProviderCallback: _ => 0x10332210);
            Registers.ChipRev.Define(this, 0x0C).WithValueField(0, 32, name: "CHIPREV", valueProviderCallback: _ => 0x22);
            Registers.VendorID.Define(this, 0x10).WithValueField(0, 32, name: "VENDORID", valueProviderCallback: _ => 0x414D4251);
            Registers.SKU.Define(this, 0x14).WithValueField(0, 32, name: "SKU", valueProviderCallback: _ => 0x2D480F);
            Registers.Reg_0x28.Define(this, 0x28).WithValueField(0, 32, name: "REG_0x28", valueProviderCallback: _ => 0x2);
            Registers.Reg_0x60.Define(this, 0x60).WithValueField(0, 32, name: "REG_0x60", valueProviderCallback: _ => 0xED9CE);
            Registers.Reg_0x120.Define(this, 0x120).WithValueField(0, 32, name: "REG_0x120", valueProviderCallback: _ => 0x18);
            Registers.Reg_0x180.Define(this, 0x180).WithValueField(0, 32, name: "REG_0x180", valueProviderCallback: _ => 0x300);
            Registers.Reg_0x1AC.Define(this, 0x1AC).WithValueField(0, 32, name: "REG_0x1AC", valueProviderCallback: _ => 0x1E);
            Registers.Reg_0x1B0.Define(this, 0x1B0).WithValueField(0, 32, name: "REG_0x1B0", valueProviderCallback: _ => 0x83E00000);
            Registers.Reg_0x1B8.Define(this, 0x1B8).WithValueField(0, 32, name: "REG_0x1B8", valueProviderCallback: _ => 0xE);
            Registers.ShadowValid.Define(this, 0x1BC).WithValueField(0, 32, name: "SHADOWVALID", valueProviderCallback: _ => 0xD9);
            Registers.Reg_0x37C.Define(this, 0x37C).WithValueField(0, 32, name: "REG_0x37C", valueProviderCallback: _ => 0x40000000);
            Registers.Reg_0x380.Define(this, 0x380).WithValueField(0, 32, name: "REG_0x380", valueProviderCallback: _ => 0x11000);
            Registers.Reg_0x448.Define(this, 0x448).WithValueField(0, 32, name: "REG_0x448", valueProviderCallback: _ => 0x101);
        }

        private enum Registers : long
        {
            ChipPN = 0x00,
            ChipRev = 0x0C,
            VendorID = 0x10,
            SKU = 0x14,
            Reg_0x28 = 0x28,
            Reg_0x60 = 0x60,
            Reg_0x120 = 0x120,
            Reg_0x180 = 0x180,
            Reg_0x1AC = 0x1AC,
            Reg_0x1B0 = 0x1B0,
            Reg_0x1B8 = 0x1B8,
            ShadowValid = 0x1BC,
            Reg_0x37C = 0x37C,
            Reg_0x380 = 0x380,
            Reg_0x448 = 0x448,
        }
    }
}
