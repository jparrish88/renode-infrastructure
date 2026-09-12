//
// Copyright (c) 2010-2022 Antmicro
//
// This file is licensed under the MIT License.
// Full license text is available in 'licenses/MIT.txt'.
//
// Apollo510 Lite MCU Miscellaneous Control Logic (MCUCTRL @ 0x4000A800).
// NEW file for the Lite family — does not modify AmbiqApollo510_MCUCTRL.
//
// All 88 registers from pack/SVD/apollo510L.svd. Read-only model
// (SCRATCH0/1 are real RW): RAZ/WI otherwise.
//
// Identity values are LIVE-SILICON verified (JLink OB, AP510DLA EVB,
// 2026-09-11) and banner-tested on hello_world_uart:
//   CHIPPN 0x111101A0 (PN=0x11, DEVTYPE=0 => Apollo510 Lite),
//   CHIPREV 0x000E9512 (RevA1), VENDORID 0x414D4251 'AMBQ',
//   SKU 0x002C93D5, SHADOWVALID 0x6B (VALID|INFO1SELOTP|INFOCSELOTP|OTPREADY).
// CHIPID0/1 are the sampled die's unique ID. ACRG/D2ASPARE/BOOTLOADER keep
// SVD resets (analog/trim values vary per die; live D2ASPARE=0x80000000,
// BOOTLOADER=0xA4000005, ACRG=0x88 noted for follow-up).
//
using Antmicro.Renode.Core;
using Antmicro.Renode.Core.Structure.Registers;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class AmbiqApollo510L_MCUCTRL : BasicDoubleWordPeripheral, IKnownSize
    {
        public AmbiqApollo510L_MCUCTRL(IMachine machine) : base(machine)
        {
            DefineRegisters();
        }

        public long Size => 0x500;

        private void DefineRegisters()
        {
        // 88 registers from apollo510L.svd.
        Registers.CHIPPN.Define(this, 0x0).WithValueField(0, 32, name: "CHIPPN", valueProviderCallback: _ => 0x111101A0); // LIVE (SVD reset 0x11910080)
        Registers.CHIPID0.Define(this, 0x4).WithValueField(0, 32, name: "CHIPID0", valueProviderCallback: _ => 0x70C3228A); // LIVE (SVD reset 0x00000000)
        Registers.CHIPID1.Define(this, 0x8).WithValueField(0, 32, name: "CHIPID1", valueProviderCallback: _ => 0x843205F1); // LIVE (SVD reset 0x00000000)
        Registers.CHIPREV.Define(this, 0xC).WithValueField(0, 32, name: "CHIPREV", valueProviderCallback: _ => 0xE9512); // LIVE (SVD reset 0x00000012)
        Registers.VENDORID.Define(this, 0x10).WithValueField(0, 32, name: "VENDORID", valueProviderCallback: _ => 0x414D4251); // LIVE (SVD reset 0x00000000)
        Registers.SKU.Define(this, 0x14).WithValueField(0, 32, name: "SKU", valueProviderCallback: _ => 0x2C93D5); // LIVE (SVD reset 0x002C8005)
        Registers.DEBUGGER.Define(this, 0x20).WithValueField(0, 32, name: "DEBUGGER", valueProviderCallback: _ => 0x0);
        Registers.ACRG.Define(this, 0x28).WithValueField(0, 32, name: "ACRG", valueProviderCallback: _ => 0x78);
        Registers.VREFGEN2.Define(this, 0x44).WithValueField(0, 32, name: "VREFGEN2", valueProviderCallback: _ => 0xFFC0);
        Registers.VREFGEN3.Define(this, 0x48).WithValueField(0, 32, name: "VREFGEN3", valueProviderCallback: _ => 0xF840);
        Registers.VREFGEN4.Define(this, 0x4C).WithValueField(0, 32, name: "VREFGEN4", valueProviderCallback: _ => 0x7FEFC0);
        Registers.VREFGEN5.Define(this, 0x50).WithValueField(0, 32, name: "VREFGEN5", valueProviderCallback: _ => 0x7DF7C0);
        Registers.VREFBUF.Define(this, 0x54).WithValueField(0, 32, name: "VREFBUF", valueProviderCallback: _ => 0x0);
        Registers.VRCTRL.Define(this, 0x60).WithValueField(0, 32, name: "VRCTRL", valueProviderCallback: _ => 0x0);
        Registers.LDOREG1.Define(this, 0x80).WithValueField(0, 32, name: "LDOREG1", valueProviderCallback: _ => 0x6D3D4);
        Registers.LDOREG2.Define(this, 0x88).WithValueField(0, 32, name: "LDOREG2", valueProviderCallback: _ => 0x20000774);
        Registers.HFRC.Define(this, 0xC0).WithValueField(0, 32, name: "HFRC", valueProviderCallback: _ => 0x2);
        Registers.LFRC.Define(this, 0xE0).WithValueField(0, 32, name: "LFRC", valueProviderCallback: _ => 0x520);
        Registers.BODCTRL.Define(this, 0x100).WithValueField(0, 32, name: "BODCTRL", valueProviderCallback: _ => 0x0);
        Registers.ADCPWRCTRL.Define(this, 0x108).WithValueField(0, 32, name: "ADCPWRCTRL", valueProviderCallback: _ => 0x0);
        Registers.ADCCAL.Define(this, 0x10C).WithValueField(0, 32, name: "ADCCAL", valueProviderCallback: _ => 0x1);
        Registers.ADCBATTLOAD.Define(this, 0x110).WithValueField(0, 32, name: "ADCBATTLOAD", valueProviderCallback: _ => 0x0);
        Registers.XTALCTRL.Define(this, 0x120).WithValueField(0, 32, name: "XTALCTRL", valueProviderCallback: _ => 0x1B8);
        Registers.XTALGENCTRL.Define(this, 0x124).WithValueField(0, 32, name: "XTALGENCTRL", valueProviderCallback: _ => 0x1E0);
        Registers.XTALHSCTRL.Define(this, 0x12C).WithValueField(0, 32, name: "XTALHSCTRL", valueProviderCallback: _ => 0x0);
        Registers.MRAMCRYPTOPWRCTRL.Define(this, 0x180).WithValueField(0, 32, name: "MRAMCRYPTOPWRCTRL", valueProviderCallback: _ => 0x0);
        Registers.BODISABLE.Define(this, 0x1AC).WithValueField(0, 32, name: "BODISABLE", valueProviderCallback: _ => 0x0);
        Registers.D2ASPARE.Define(this, 0x1B0).WithValueField(0, 32, name: "D2ASPARE", valueProviderCallback: _ => 0x18000);
        Registers.BOOTLOADER.Define(this, 0x1B8).WithValueField(0, 32, name: "BOOTLOADER", valueProviderCallback: _ => 0xF);
        Registers.SHADOWVALID.Define(this, 0x1BC).WithValueField(0, 32, name: "SHADOWVALID", valueProviderCallback: _ => 0x6B); // LIVE (SVD reset 0x00000002)
        Registers.SCRATCH0.Define(this, 0x1C0).WithValueField(0, 32, out _, name: "SCRATCH0"); // RW scratch
        Registers.SCRATCH1.Define(this, 0x1C4).WithValueField(0, 32, out _, name: "SCRATCH1"); // RW scratch
        Registers.DBGR1.Define(this, 0x200).WithValueField(0, 32, name: "DBGR1", valueProviderCallback: _ => 0x12345678);
        Registers.DBGR2.Define(this, 0x204).WithValueField(0, 32, name: "DBGR2", valueProviderCallback: _ => 0xC001C0DE);
        Registers.WICCONTROL.Define(this, 0x21C).WithValueField(0, 32, name: "WICCONTROL", valueProviderCallback: _ => 0x1);
        Registers.DBGCTRL.Define(this, 0x250).WithValueField(0, 32, name: "DBGCTRL", valueProviderCallback: _ => 0x4105);
        Registers.OTAPOINTER.Define(this, 0x264).WithValueField(0, 32, name: "OTAPOINTER", valueProviderCallback: _ => 0x0);
        Registers.APBDMACTRL.Define(this, 0x280).WithValueField(0, 32, name: "APBDMACTRL", valueProviderCallback: _ => 0x203);
        Registers.FORCEAXICLKEN.Define(this, 0x284).WithValueField(0, 32, name: "FORCEAXICLKEN", valueProviderCallback: _ => 0x6);
        Registers.KEXTCLKSEL.Define(this, 0x338).WithValueField(0, 32, name: "KEXTCLKSEL", valueProviderCallback: _ => 0x0);
        Registers.SIMOBUCK0.Define(this, 0x33C).WithValueField(0, 32, name: "SIMOBUCK0", valueProviderCallback: _ => 0x7FF80);
        Registers.SIMOBUCK1.Define(this, 0x340).WithValueField(0, 32, name: "SIMOBUCK1", valueProviderCallback: _ => 0x80469);
        Registers.SIMOBUCK2.Define(this, 0x344).WithValueField(0, 32, name: "SIMOBUCK2", valueProviderCallback: _ => 0x14941DEB);
        Registers.SIMOBUCK3.Define(this, 0x348).WithValueField(0, 32, name: "SIMOBUCK3", valueProviderCallback: _ => 0xEB499CA);
        Registers.SIMOBUCK4.Define(this, 0x34C).WithValueField(0, 32, name: "SIMOBUCK4", valueProviderCallback: _ => 0x1519AA);
        Registers.SIMOBUCK6.Define(this, 0x354).WithValueField(0, 32, name: "SIMOBUCK6", valueProviderCallback: _ => 0x0);
        Registers.SIMOBUCK7.Define(this, 0x358).WithValueField(0, 32, name: "SIMOBUCK7", valueProviderCallback: _ => 0x1000000);
        Registers.SIMOBUCK8.Define(this, 0x35C).WithValueField(0, 32, name: "SIMOBUCK8", valueProviderCallback: _ => 0x10731927);
        Registers.SIMOBUCK9.Define(this, 0x360).WithValueField(0, 32, name: "SIMOBUCK9", valueProviderCallback: _ => 0xA831547);
        Registers.SIMOBUCK10.Define(this, 0x364).WithValueField(0, 32, name: "SIMOBUCK10", valueProviderCallback: _ => 0x1589);
        Registers.SIMOBUCK11.Define(this, 0x368).WithValueField(0, 32, name: "SIMOBUCK11", valueProviderCallback: _ => 0x0);
        Registers.D2ASPARE2.Define(this, 0x36C).WithValueField(0, 32, name: "D2ASPARE2", valueProviderCallback: _ => 0x0);
        Registers.USBLDOCTRL.Define(this, 0x374).WithValueField(0, 32, name: "USBLDOCTRL", valueProviderCallback: _ => 0x0);
        Registers.I3CPHYCTRL.Define(this, 0x378).WithValueField(0, 32, name: "I3CPHYCTRL", valueProviderCallback: _ => 0x0);
        Registers.PWRSW0.Define(this, 0x37C).WithValueField(0, 32, name: "PWRSW0", valueProviderCallback: _ => 0x100);
        Registers.PWRSW1.Define(this, 0x380).WithValueField(0, 32, name: "PWRSW1", valueProviderCallback: _ => 0x0);
        Registers.USBRSTCTRL.Define(this, 0x388).WithValueField(0, 32, name: "USBRSTCTRL", valueProviderCallback: _ => 0x0);
        Registers.FLASHWPROT0.Define(this, 0x3A8).WithValueField(0, 32, name: "FLASHWPROT0", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHWPROT1.Define(this, 0x3AC).WithValueField(0, 32, name: "FLASHWPROT1", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHWPROT2.Define(this, 0x3B0).WithValueField(0, 32, name: "FLASHWPROT2", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHWPROT3.Define(this, 0x3B4).WithValueField(0, 32, name: "FLASHWPROT3", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHRPROT0.Define(this, 0x3B8).WithValueField(0, 32, name: "FLASHRPROT0", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHRPROT1.Define(this, 0x3BC).WithValueField(0, 32, name: "FLASHRPROT1", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHRPROT2.Define(this, 0x3C0).WithValueField(0, 32, name: "FLASHRPROT2", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.FLASHRPROT3.Define(this, 0x3C4).WithValueField(0, 32, name: "FLASHRPROT3", valueProviderCallback: _ => 0xFFFFFFFF);
        Registers.SRAMWPROT0.Define(this, 0x3C8).WithValueField(0, 32, name: "SRAMWPROT0", valueProviderCallback: _ => 0x0);
        Registers.SRAMWPROT1.Define(this, 0x3CC).WithValueField(0, 32, name: "SRAMWPROT1", valueProviderCallback: _ => 0x0);
        Registers.SRAMWPROT2.Define(this, 0x3D0).WithValueField(0, 32, name: "SRAMWPROT2", valueProviderCallback: _ => 0x0);
        Registers.SRAMWPROT3.Define(this, 0x3D4).WithValueField(0, 32, name: "SRAMWPROT3", valueProviderCallback: _ => 0x0);
        Registers.SRAMRPROT0.Define(this, 0x3F0).WithValueField(0, 32, name: "SRAMRPROT0", valueProviderCallback: _ => 0x0);
        Registers.SRAMRPROT1.Define(this, 0x3F4).WithValueField(0, 32, name: "SRAMRPROT1", valueProviderCallback: _ => 0x0);
        Registers.SRAMRPROT2.Define(this, 0x3F8).WithValueField(0, 32, name: "SRAMRPROT2", valueProviderCallback: _ => 0x0);
        Registers.SRAMRPROT3.Define(this, 0x3FC).WithValueField(0, 32, name: "SRAMRPROT3", valueProviderCallback: _ => 0x0);
        Registers.SDIO0CTRL.Define(this, 0x454).WithValueField(0, 32, name: "SDIO0CTRL", valueProviderCallback: _ => 0x1080);
        Registers.SDIO1CTRL.Define(this, 0x458).WithValueField(0, 32, name: "SDIO1CTRL", valueProviderCallback: _ => 0x1080);
        Registers.PDMCTRL.Define(this, 0x45C).WithValueField(0, 32, name: "PDMCTRL", valueProviderCallback: _ => 0x1);
        Registers.DSIBIST.Define(this, 0x4A0).WithValueField(0, 32, name: "DSIBIST", valueProviderCallback: _ => 0x404);
        Registers.SSRAMMISCCTRL.Define(this, 0x4A4).WithValueField(0, 32, name: "SSRAMMISCCTRL", valueProviderCallback: _ => 0x7);
        Registers.DISPSTATUS.Define(this, 0x4B0).WithValueField(0, 32, name: "DISPSTATUS", valueProviderCallback: _ => 0x0);
        Registers.CPUCFG.Define(this, 0x4CC).WithValueField(0, 32, name: "CPUCFG", valueProviderCallback: _ => 0x0);
        Registers.PLLCTL0.Define(this, 0x4D8).WithValueField(0, 32, name: "PLLCTL0", valueProviderCallback: _ => 0xC000001F);
        Registers.PLLDIV0.Define(this, 0x4DC).WithValueField(0, 32, name: "PLLDIV0", valueProviderCallback: _ => 0x6AAAAB);
        Registers.PLLDIV1.Define(this, 0x4E0).WithValueField(0, 32, name: "PLLDIV1", valueProviderCallback: _ => 0x1102);
        Registers.PLLSTAT.Define(this, 0x4E4).WithValueField(0, 32, name: "PLLSTAT", valueProviderCallback: _ => 0x0);
        Registers.PLLMUXCTL.Define(this, 0x4E8).WithValueField(0, 32, name: "PLLMUXCTL", valueProviderCallback: _ => 0xE);
        Registers.CM4CODEBASE.Define(this, 0x4F0).WithValueField(0, 32, name: "CM4CODEBASE", valueProviderCallback: _ => 0x0);
        Registers.RADIOFINECNT.Define(this, 0x4F4).WithValueField(0, 32, name: "RADIOFINECNT", valueProviderCallback: _ => 0x0);
        Registers.RADIOCLKNCNT.Define(this, 0x4F8).WithValueField(0, 32, name: "RADIOCLKNCNT", valueProviderCallback: _ => 0x0);
        }

        private enum Registers : long
        {
        CHIPPN = 0x0,
        CHIPID0 = 0x4,
        CHIPID1 = 0x8,
        CHIPREV = 0xC,
        VENDORID = 0x10,
        SKU = 0x14,
        DEBUGGER = 0x20,
        ACRG = 0x28,
        VREFGEN2 = 0x44,
        VREFGEN3 = 0x48,
        VREFGEN4 = 0x4C,
        VREFGEN5 = 0x50,
        VREFBUF = 0x54,
        VRCTRL = 0x60,
        LDOREG1 = 0x80,
        LDOREG2 = 0x88,
        HFRC = 0xC0,
        LFRC = 0xE0,
        BODCTRL = 0x100,
        ADCPWRCTRL = 0x108,
        ADCCAL = 0x10C,
        ADCBATTLOAD = 0x110,
        XTALCTRL = 0x120,
        XTALGENCTRL = 0x124,
        XTALHSCTRL = 0x12C,
        MRAMCRYPTOPWRCTRL = 0x180,
        BODISABLE = 0x1AC,
        D2ASPARE = 0x1B0,
        BOOTLOADER = 0x1B8,
        SHADOWVALID = 0x1BC,
        SCRATCH0 = 0x1C0,
        SCRATCH1 = 0x1C4,
        DBGR1 = 0x200,
        DBGR2 = 0x204,
        WICCONTROL = 0x21C,
        DBGCTRL = 0x250,
        OTAPOINTER = 0x264,
        APBDMACTRL = 0x280,
        FORCEAXICLKEN = 0x284,
        KEXTCLKSEL = 0x338,
        SIMOBUCK0 = 0x33C,
        SIMOBUCK1 = 0x340,
        SIMOBUCK2 = 0x344,
        SIMOBUCK3 = 0x348,
        SIMOBUCK4 = 0x34C,
        SIMOBUCK6 = 0x354,
        SIMOBUCK7 = 0x358,
        SIMOBUCK8 = 0x35C,
        SIMOBUCK9 = 0x360,
        SIMOBUCK10 = 0x364,
        SIMOBUCK11 = 0x368,
        D2ASPARE2 = 0x36C,
        USBLDOCTRL = 0x374,
        I3CPHYCTRL = 0x378,
        PWRSW0 = 0x37C,
        PWRSW1 = 0x380,
        USBRSTCTRL = 0x388,
        FLASHWPROT0 = 0x3A8,
        FLASHWPROT1 = 0x3AC,
        FLASHWPROT2 = 0x3B0,
        FLASHWPROT3 = 0x3B4,
        FLASHRPROT0 = 0x3B8,
        FLASHRPROT1 = 0x3BC,
        FLASHRPROT2 = 0x3C0,
        FLASHRPROT3 = 0x3C4,
        SRAMWPROT0 = 0x3C8,
        SRAMWPROT1 = 0x3CC,
        SRAMWPROT2 = 0x3D0,
        SRAMWPROT3 = 0x3D4,
        SRAMRPROT0 = 0x3F0,
        SRAMRPROT1 = 0x3F4,
        SRAMRPROT2 = 0x3F8,
        SRAMRPROT3 = 0x3FC,
        SDIO0CTRL = 0x454,
        SDIO1CTRL = 0x458,
        PDMCTRL = 0x45C,
        DSIBIST = 0x4A0,
        SSRAMMISCCTRL = 0x4A4,
        DISPSTATUS = 0x4B0,
        CPUCFG = 0x4CC,
        PLLCTL0 = 0x4D8,
        PLLDIV0 = 0x4DC,
        PLLDIV1 = 0x4E0,
        PLLSTAT = 0x4E4,
        PLLMUXCTL = 0x4E8,
        CM4CODEBASE = 0x4F0,
        RADIOFINECNT = 0x4F4,
        RADIOCLKNCNT = 0x4F8,        }
    }
}
