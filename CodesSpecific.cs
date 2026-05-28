using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic;
using Microsoft.VisualBasic.CompilerServices;
using Microsoft.VisualBasic.FileIO;

namespace AutomacoesCivil3D;

[StandardModule]
public sealed class CodesSpecific
{
	public struct CodeType
	{
		public string Code;

		public int Index;

		public string Description;
	}

	public struct AllCodes
	{
		public bool CodesStructureFilled;

		public CodeType Crown;

		public CodeType CrownPave1;

		public CodeType CrownPave2;

		public CodeType CrownBase;

		public CodeType CrownSub;

		public CodeType ETW;

		public CodeType ETWPave1;

		public CodeType ETWPave2;

		public CodeType ETWBase;

		public CodeType ETWSub;

		public CodeType Lane;

		public CodeType LanePave1;

		public CodeType LanePave2;

		public CodeType LaneBase;

		public CodeType LaneSub;

		public CodeType EPS;

		public CodeType EPSPave1;

		public CodeType EPSPave2;

		public CodeType EPSBase;

		public CodeType EPSSub;

		public CodeType EPSBaseIn;

		public CodeType EPSSubIn;

		public CodeType ESUnpaved;

		public CodeType DaylightSub;

		public CodeType Daylight;

		public CodeType DaylightFill;

		public CodeType DaylightCut;

		public CodeType DitchIn;

		public CodeType DitchOut;

		public CodeType BenchIn;

		public CodeType BenchOut;

		public CodeType FlowlineDitch;

		public CodeType LMedDitch;

		public CodeType RMedDitch;

		public CodeType Flange;

		public CodeType Flowline_Gutter;

		public CodeType TopCurb;

		public CodeType BottomCurb;

		public CodeType BackCurb;

		public CodeType SidewalkIn;

		public CodeType SidewalkOut;

		public CodeType HingeCut;

		public CodeType HingeFill;

		public CodeType Top;

		public CodeType Datum;

		public CodeType Pave;

		public CodeType Pave1;

		public CodeType Pave2;

		public CodeType Base;

		public CodeType SubBase;

		public CodeType Gravel;

		public CodeType TopCurbNew;

		public CodeType BackCurbNew;

		public CodeType Curb;

		public CodeType Sidewalk;

		public CodeType Hinge;

		public CodeType EOV;

		public CodeType EOVOverlay;

		public CodeType Level;

		public CodeType Mill;

		public CodeType Overlay;

		public CodeType CrownOverlay;

		public CodeType Barrier;

		public CodeType EBD;

		public CodeType CrownDeck;

		public CodeType Deck;

		public CodeType Girder;

		public CodeType EBS;

		public CodeType ESL;

		public CodeType DaylightBallast;

		public CodeType ESBS;

		public CodeType DaylightSubballast;

		public CodeType Ballast;

		public CodeType Sleeper;

		public CodeType Subballast;

		public CodeType Rail;

		public CodeType R1;

		public CodeType R2;

		public CodeType R3;

		public CodeType R4;

		public CodeType R5;

		public CodeType R6;

		public CodeType Bridge;

		public CodeType Ditch;

		public CodeType CrownFin;

		public CodeType CrownSubBase;

		public CodeType ETWSubBase;

		public CodeType MarkedPoint;

		public CodeType Guardrail;

		public CodeType Median;

		public CodeType ETWOverlay;

		public CodeType TrenchBottom;

		public CodeType TrenchDaylight;

		public CodeType TrenchBedding;

		public CodeType TrenchBackfill;

		public CodeType Trench;

		public CodeType LaneBreak;

		public CodeType LaneBreakOverlay;

		public CodeType Sod;

		public CodeType DaylightStrip;

		public CodeType sForeslopeStripping;

		public CodeType Stripping;

		public CodeType ChannelFlowline;

		public CodeType Channe_Bottom;

		public CodeType ChannelTop;

		public CodeType ChannelExtension;

		public CodeType ChannelBackslope;

		public CodeType LiningMaterial;

		public CodeType DitchBack;

		public CodeType DitchFace;

		public CodeType DitchTop;

		public CodeType DitchBottom;

		public CodeType Backfill;

		public CodeType BackfillFace;

		public CodeType DitchLidFace;

		public CodeType LidTop;

		public CodeType DitchBackFill;

		public CodeType Lid;

		public CodeType DrainBottom;

		public CodeType DrainBottomOutside;

		public CodeType DrainTopOutside;

		public CodeType DrainTopInside;

		public CodeType DrainBottomInside;

		public CodeType DrainCenter;

		public CodeType FlowLine;

		public CodeType DrainTop;

		public CodeType DrainStructure;

		public CodeType DrainArea;

		public CodeType RWFront;

		public CodeType RWTop;

		public CodeType RWBack;

		public CodeType RWHinge;

		public CodeType RWInside;

		public CodeType RWOutside;

		public CodeType Wall;

		public CodeType RWall;

		public CodeType RWallB1;

		public CodeType RWallB2;

		public CodeType RWallB3;

		public CodeType RWallB4;

		public CodeType RWallK1;

		public CodeType RWallK2;

		public CodeType FootingBottom;

		public CodeType WalkEdge;

		public CodeType Lot;

		public CodeType Slope_Link;

		public CodeType Channel_Side;

		public CodeType Bench;

		public CodeType CrownPave3;

		public CodeType LanePave3;

		public CodeType ETWBase1;

		public CodeType CrownBase1;

		public CodeType LaneBase1;

		public CodeType ETWBase2;

		public CodeType CrownBase2;

		public CodeType LaneBase2;

		public CodeType ETWBase3;

		public CodeType CrownBase3;

		public CodeType LaneBase3;

		public CodeType ETWSub1;

		public CodeType CrownSub1;

		public CodeType LaneSub1;

		public CodeType ETWSub2;

		public CodeType CrownSub2;

		public CodeType LaneSub2;

		public CodeType ETWSub3;

		public CodeType CrownSub3;

		public CodeType LaneSub3;

		public CodeType Pave3;

		public CodeType Base1;

		public CodeType Base2;

		public CodeType Base3;

		public CodeType Subbase1;

		public CodeType Subbase2;

		public CodeType Subbase3;

		public CodeType EPSBase1;

		public CodeType EPSBase2;

		public CodeType EPSBase3;

		public CodeType EPSSubBase1;

		public CodeType EPSSubBase2;

		public CodeType EPSSubBase3;

		public CodeType ETWPave3;

		public CodeType EPSBase4;

		public CodeType Base4;

		public CodeType SR;

		public CodeType EPSPave3;

		public CodeType EOVMilling;

		public CodeType EOVLeveling;

		public CodeType CrownLeveling;

		public CodeType CrownMilling;

		public CodeType EOVMillingInner;

		public CodeType EOVMillingOuter;
	}

	private const string constCodesFile = "C3DStockSubassemblyScripts.codes";

	private static string[] CodesDefault = new string[189];

	public static AllCodes Codes;

	[DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetPrivateProfileString([MarshalAs(UnmanagedType.VBByRefStr)] ref string lpAppName, [MarshalAs(UnmanagedType.VBByRefStr)] ref string lpKeyName, [MarshalAs(UnmanagedType.VBByRefStr)] ref string lpDefault, StringBuilder lpReturnedString, int nSize, [MarshalAs(UnmanagedType.VBByRefStr)] ref string lpFileName);

	private static void InitializeDefaults()
	{
		CodesDefault[1] = "Crown";
		CodesDefault[2] = "Crown_Pave1";
		CodesDefault[3] = "Crown_Pave2";
		CodesDefault[4] = "Crown_Base";
		CodesDefault[5] = "Crown_Sub";
		CodesDefault[6] = "ETW";
		CodesDefault[7] = "ETW_Pave1";
		CodesDefault[8] = "ETW_Pave2";
		CodesDefault[9] = "ETW_Base";
		CodesDefault[10] = "ETW_Sub";
		CodesDefault[11] = "Lane";
		CodesDefault[12] = "Lane_Pave1";
		CodesDefault[13] = "Lane_Pave2";
		CodesDefault[14] = "Lane_Base";
		CodesDefault[15] = "Lane_Sub";
		CodesDefault[16] = "EPS";
		CodesDefault[17] = "EPS_Pave1";
		CodesDefault[18] = "EPS_Pave2";
		CodesDefault[19] = "EPS_Base";
		CodesDefault[20] = "EPS_Sub";
		CodesDefault[21] = "EPS_Base_In";
		CodesDefault[22] = "EPS_Sub_In";
		CodesDefault[23] = "ES_Unpaved";
		CodesDefault[24] = "Daylight_Sub";
		CodesDefault[25] = "Daylight";
		CodesDefault[26] = "Daylight_Fill";
		CodesDefault[27] = "Daylight_Cut";
		CodesDefault[28] = "Ditch_In";
		CodesDefault[29] = "Ditch_Out";
		CodesDefault[30] = "Bench_In";
		CodesDefault[31] = "Bench_Out";
		CodesDefault[32] = "Flowline_Ditch";
		CodesDefault[33] = "LMedDitch";
		CodesDefault[34] = "RMedDitch";
		CodesDefault[35] = "Flange";
		CodesDefault[36] = "Flowline_Gutter";
		CodesDefault[37] = "Top_Curb";
		CodesDefault[38] = "Bottom_Curb";
		CodesDefault[39] = "Back_Curb";
		CodesDefault[40] = "Sidewalk_In";
		CodesDefault[41] = "Sidewalk_Out";
		CodesDefault[42] = "Hinge_Cut";
		CodesDefault[43] = "Hinge_Fill";
		CodesDefault[44] = "Top";
		CodesDefault[45] = "Datum";
		CodesDefault[46] = "Pave";
		CodesDefault[47] = "Pave1";
		CodesDefault[48] = "Pave2";
		CodesDefault[49] = "Base";
		CodesDefault[50] = "SubBase";
		CodesDefault[51] = "Gravel";
		CodesDefault[52] = "Top_Curb";
		CodesDefault[53] = "Back_Curb";
		CodesDefault[54] = "Curb";
		CodesDefault[55] = "Sidewalk";
		CodesDefault[56] = "Hinge";
		CodesDefault[57] = "EOV";
		CodesDefault[58] = "EOV_Overlay";
		CodesDefault[59] = "Level";
		CodesDefault[60] = "Mill";
		CodesDefault[61] = "Overlay";
		CodesDefault[62] = "Crown_Overlay";
		CodesDefault[63] = "Barrier";
		CodesDefault[64] = "EBD";
		CodesDefault[65] = "Crown_Deck";
		CodesDefault[66] = "Deck";
		CodesDefault[67] = "Girder";
		CodesDefault[68] = "EBS";
		CodesDefault[69] = "ESL";
		CodesDefault[70] = "Daylight_Ballast";
		CodesDefault[71] = "ESBS";
		CodesDefault[72] = "Daylight_Subballast";
		CodesDefault[73] = "Ballast";
		CodesDefault[74] = "Sleeper";
		CodesDefault[75] = "Subballast";
		CodesDefault[76] = "Rail";
		CodesDefault[77] = "R1";
		CodesDefault[78] = "R2";
		CodesDefault[79] = "R3";
		CodesDefault[80] = "R4";
		CodesDefault[81] = "R5";
		CodesDefault[82] = "R6";
		CodesDefault[83] = "Bridge";
		CodesDefault[84] = "Ditch";
		CodesDefault[85] = "Crown_Fin";
		CodesDefault[86] = "Crown_SubBase";
		CodesDefault[87] = "ETW_SubBase";
		CodesDefault[88] = "MarkedPoint";
		CodesDefault[89] = "Guardrail";
		CodesDefault[90] = "Median";
		CodesDefault[91] = "ETW_Overlay";
		CodesDefault[92] = "Trench_Bottom";
		CodesDefault[93] = "Trench_Daylight";
		CodesDefault[94] = "Trench_Bedding";
		CodesDefault[95] = "Trench_Backfill";
		CodesDefault[96] = "Trench";
		CodesDefault[97] = "LaneBreak";
		CodesDefault[98] = "LaneBreak_Overlay";
		CodesDefault[99] = "Sod";
		CodesDefault[100] = "Daylight_Strip";
		CodesDefault[101] = "Foreslope_Stripping";
		CodesDefault[102] = "Stripping";
		CodesDefault[103] = "Channel_Flowline";
		CodesDefault[104] = "Channel_Bottom";
		CodesDefault[105] = "Channel_Top";
		CodesDefault[106] = "Channel_Extension";
		CodesDefault[107] = "Channel_Backslope";
		CodesDefault[108] = "Lining_Material";
		CodesDefault[109] = "Ditch_Back";
		CodesDefault[110] = "Ditch_Face";
		CodesDefault[111] = "Ditch_Top";
		CodesDefault[112] = "Ditch_Bottom";
		CodesDefault[113] = "Backfill";
		CodesDefault[114] = "Backfill_Face";
		CodesDefault[115] = "Ditch_Lid_Face";
		CodesDefault[116] = "Lid_Top";
		CodesDefault[117] = "Ditch_Back_Fill";
		CodesDefault[118] = "Lid";
		CodesDefault[119] = "Drain_Bottom";
		CodesDefault[120] = "Drain_Top_Outside";
		CodesDefault[121] = "Drain_Top_Outside";
		CodesDefault[122] = "Drain_Top_Inside";
		CodesDefault[123] = "Drain_Bottom_Inside";
		CodesDefault[124] = "Drain_Center";
		CodesDefault[125] = "Flow_Line";
		CodesDefault[126] = "Drain_Top";
		CodesDefault[127] = "Drain_Structure";
		CodesDefault[128] = "Drain_Area";
		CodesDefault[129] = "RW_Front";
		CodesDefault[130] = "RW_Top";
		CodesDefault[131] = "RW_Back";
		CodesDefault[132] = "RW_Hinge";
		CodesDefault[133] = "RW_Inside";
		CodesDefault[134] = "RW_Outside";
		CodesDefault[135] = "Wall";
		CodesDefault[136] = "RWall";
		CodesDefault[137] = "RWall_B1";
		CodesDefault[138] = "RWall_B2";
		CodesDefault[139] = "RWall_B3";
		CodesDefault[140] = "RWall_B4";
		CodesDefault[141] = "RWall_K1";
		CodesDefault[142] = "RWall_K2";
		CodesDefault[143] = "Footing_Bottom";
		CodesDefault[144] = "Walk_Edge";
		CodesDefault[145] = "Lot";
		CodesDefault[146] = "Slope_Link";
		CodesDefault[147] = "Channel_Side";
		CodesDefault[148] = "Bench";
		CodesDefault[149] = "Crown_Pave3";
		CodesDefault[150] = "Lane_Pave3";
		CodesDefault[151] = "ETW_Base1";
		CodesDefault[152] = "Crown_Base1";
		CodesDefault[153] = "Lane_Base1";
		CodesDefault[154] = "ETW_Base2";
		CodesDefault[155] = "Crown_Base2";
		CodesDefault[156] = "Lane_Base2";
		CodesDefault[157] = "ETW_Base3";
		CodesDefault[158] = "Crown_Base3";
		CodesDefault[159] = "Lane_Base3";
		CodesDefault[160] = "ETW_Sub1";
		CodesDefault[161] = "Crown_Sub1";
		CodesDefault[162] = "Lane_Sub1";
		CodesDefault[163] = "ETW_Sub2";
		CodesDefault[164] = "Crown_Sub2";
		CodesDefault[165] = "Lane_Sub2";
		CodesDefault[166] = "ETW_Sub3";
		CodesDefault[167] = "Crown_Sub3";
		CodesDefault[168] = "Lane_Sub3";
		CodesDefault[169] = "Pave3";
		CodesDefault[170] = "Base1";
		CodesDefault[171] = "Base2";
		CodesDefault[172] = "Base3";
		CodesDefault[173] = "Subbase1";
		CodesDefault[174] = "Subbase2";
		CodesDefault[175] = "Subbase3";
		CodesDefault[176] = "EPS_Base1";
		CodesDefault[177] = "EPS_Base2";
		CodesDefault[178] = "EPS_Base3";
		CodesDefault[179] = "EPS_SubBase1";
		CodesDefault[180] = "EPS_SubBase2";
		CodesDefault[181] = "EPS_SubBase3";
		CodesDefault[182] = "ETW_Pave3";
		CodesDefault[183] = "EPS_Base4";
		CodesDefault[184] = "Base4";
		CodesDefault[185] = "SR";
		CodesDefault[186] = "EPS_Pave3";
		CodesDefault[187] = "EOV_Milling";
		CodesDefault[188] = "EOV_Leveling";
		CodesDefault[189] = "Crown_Leveling";
		CodesDefault[189] = "Crown_Milling";
		CodesDefault[189] = "EOV_Milling_Inner";
		CodesDefault[189] = "EOV_Milling_Outer";
	}

	private static void FillDefaults(Collection colCodesAndDescriptionHashtable)
	{
		int try0000_dispatch = -1;
		int num3 = default(int);
		int num = default(int);
		int num2 = default(int);
		int num5 = default(int);
		int num6 = default(int);
		while (true)
		{
			try
			{
				/*Note: ILSpy has introduced the following switch to emulate a goto from catch-block to try-block*/;
				switch (try0000_dispatch)
				{
				default:
					ProjectData.ClearProjectError();
					num3 = 1;
					goto IL_0007;
				case 119:
					{
						num = num2;
						switch (num3)
						{
						case 1:
							break;
						default:
							goto end_IL_0000;
						}
						int num4 = num + 1;
						num = 0;
						switch (num4)
						{
						case 1:
							break;
						case 2:
							goto IL_0007;
						case 3:
							goto IL_000e;
						case 4:
							goto IL_0021;
						case 5:
							goto IL_0042;
						default:
							goto end_IL_0000;
						case 6:
							goto end_IL_0000_2;
						}
						goto default;
					}
					IL_0042:
					num2 = 5;
					num5 = checked(num5 + 1);
					goto IL_0048;
					IL_0007:
					num2 = 2;
					InitializeDefaults();
					goto IL_000e;
					IL_000e:
					num2 = 3;
					num6 = Information.UBound(CodesDefault);
					num5 = 1;
					goto IL_0048;
					IL_0048:
					if (num5 > num6)
					{
						goto end_IL_0000_2;
					}
					goto IL_0021;
					IL_0021:
					num2 = 4;
					colCodesAndDescriptionHashtable.Add(CodesDefault[num5], "I" + Conversions.ToString(num5));
					goto IL_0042;
					end_IL_0000:
					break;
				}
			}
            catch (Exception ex)
            {
                ProjectData.SetProjectError(ex);
                try0000_dispatch = 2720;   // se essa variável existir no método
                                           // se não usar mais esse dispatcher, pode simplesmente omitir essa linha
            }
            throw ProjectData.CreateProjectError(-2146828237);
			continue;
			end_IL_0000_2:
			break;
		}
		if (num != 0)
		{
			ProjectData.ClearProjectError();
		}
	}

	private static string GetCodesFilePath()
	{
		string text;
		try
		{
			text = GetCodesFilePathFromIniFile();
		}
		catch (Exception ex)
		{
			ProjectData.SetProjectError(ex);
			Exception ex2 = ex;
			text = GetConstCodesFilePath();
			ProjectData.ClearProjectError();
		}
		if (File.Exists(text))
		{
			return text;
		}
		return GetConstCodesFilePath();
	}

	private static string GetCodesFilePathFromIniFile()
	{
		string text = Interaction.Environ("AeccContent_Dir");
		if (Operators.CompareString(text, null, TextCompare: false) == 0)
		{
			return "";
		}
		string lpFileName = "";
		if (Operators.CompareString(text, "", TextCompare: false) != 0)
		{
			if (text.LastIndexOf("\\", StringComparison.Ordinal) < checked(text.Length - 1))
			{
				text += "\\";
			}
			lpFileName = text + "CodeFileName.ini";
		}
		string text2 = "";
		if (!File.Exists(lpFileName))
		{
			string text3 = "";
		}
		else
		{
			StringBuilder stringBuilder = new StringBuilder(600);
			string lpAppName = "C3D";
			string lpKeyName = "CodeFileName";
			string lpDefault = "";
			GetPrivateProfileString(ref lpAppName, ref lpKeyName, ref lpDefault, stringBuilder, stringBuilder.Capacity, ref lpFileName);
			text2 = stringBuilder.ToString();
		}
		if (File.Exists(text2))
		{
			return text2;
		}
		return "";
	}

	private static string GetConstCodesFilePath()
	{
		string text = Interaction.Environ("AeccContent_Dir");
		string text2 = ((Operators.CompareString(text, null, TextCompare: false) == 0) ? "" : text);
		if (Strings.InStrRev(text2, "\\") < text2.Length)
		{
			return text2 + "\\C3DStockSubassemblyScripts.codes";
		}
		return text2 + "C3DStockSubassemblyScripts.codes";
	}

	[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
	public static void FillCodeStructure()
	{
		int try0000_dispatch = -1;
		int num3 = default(int);
		int num = default(int);
		int num2 = default(int);
		Collection collection = default(Collection);
		int n = default(int);
		AllCodes codes = default(AllCodes);
		string codesFilePath = default(string);
		TextFieldParser textFieldParser = default(TextFieldParser);
		string text = default(string);
		string text2 = default(string);
		string item = default(string);
		while (true)
		{
			try
			{
				/*Note: ILSpy has introduced the following switch to emulate a goto from catch-block to try-block*/;
				switch (try0000_dispatch)
				{
				default:
					ProjectData.ClearProjectError();
					num3 = 1;
					goto IL_0007;
				case 5031:
					{
						num = num2;
						switch (num3)
						{
						case 1:
							break;
						default:
							goto end_IL_0000;
						}
						int num4 = num + 1;
						num = 0;
						switch (num4)
						{
						case 1:
							break;
						case 2:
							goto IL_0007;
						case 3:
							goto IL_0010;
						case 4:
							goto IL_0019;
						case 5:
							goto IL_001d;
						case 6:
							goto IL_0031;
						case 7:
							goto IL_0041;
						case 10:
							goto IL_005a;
						case 11:
							goto IL_0066;
						case 12:
							goto IL_0079;
						case 13:
							goto IL_0093;
						case 14:
							goto IL_00ae;
						case 8:
						case 9:
						case 15:
							goto IL_00c8;
						case 17:
							goto IL_00d6;
						case 16:
						case 18:
							goto IL_00e0;
						case 19:
							goto IL_00ea;
						case 20:
							goto IL_00f5;
						case 21:
							goto IL_0108;
						case 22:
							goto IL_011b;
						case 23:
							goto IL_012e;
						case 24:
							goto IL_0141;
						case 25:
							goto IL_0154;
						case 26:
							goto IL_0167;
						case 27:
							goto IL_017a;
						case 28:
							goto IL_018d;
						case 29:
							goto IL_01a0;
						case 30:
							goto IL_01b3;
						case 31:
							goto IL_01c6;
						case 32:
							goto IL_01d9;
						case 33:
							goto IL_01ec;
						case 34:
							goto IL_01ff;
						case 35:
							goto IL_0212;
						case 36:
							goto IL_0225;
						case 37:
							goto IL_0238;
						case 38:
							goto IL_024b;
						case 39:
							goto IL_025e;
						case 40:
							goto IL_0271;
						case 41:
							goto IL_0284;
						case 42:
							goto IL_0297;
						case 43:
							goto IL_02aa;
						case 44:
							goto IL_02bd;
						case 45:
							goto IL_02d0;
						case 46:
							goto IL_02e3;
						case 47:
							goto IL_02f6;
						case 48:
							goto IL_0309;
						case 49:
							goto IL_031c;
						case 50:
							goto IL_032f;
						case 51:
							goto IL_0342;
						case 52:
							goto IL_0355;
						case 53:
							goto IL_0368;
						case 54:
							goto IL_037b;
						case 55:
							goto IL_038e;
						case 56:
							goto IL_03a1;
						case 57:
							goto IL_03b4;
						case 58:
							goto IL_03c7;
						case 59:
							goto IL_03da;
						case 60:
							goto IL_03ed;
						case 61:
							goto IL_0400;
						case 62:
							goto IL_0413;
						case 63:
							goto IL_0426;
						case 64:
							goto IL_0439;
						case 65:
							goto IL_044c;
						case 66:
							goto IL_045f;
						case 67:
							goto IL_0472;
						case 68:
							goto IL_0485;
						case 69:
							goto IL_0498;
						case 70:
							goto IL_04ab;
						case 71:
							goto IL_04be;
						case 72:
							goto IL_04d1;
						case 73:
							goto IL_04e4;
						case 74:
							goto IL_04f7;
						case 75:
							goto IL_050a;
						case 76:
							goto IL_051d;
						case 77:
							goto IL_0530;
						case 78:
							goto IL_0543;
						case 79:
							goto IL_0556;
						case 80:
							goto IL_0569;
						case 81:
							goto IL_057c;
						case 82:
							goto IL_058f;
						case 83:
							goto IL_05a2;
						case 84:
							goto IL_05b5;
						case 85:
							goto IL_05c8;
						case 86:
							goto IL_05db;
						case 87:
							goto IL_05ee;
						case 88:
							goto IL_0601;
						case 89:
							goto IL_0614;
						case 90:
							goto IL_0627;
						case 91:
							goto IL_063a;
						case 92:
							goto IL_064d;
						case 93:
							goto IL_0660;
						case 94:
							goto IL_0673;
						case 95:
							goto IL_0686;
						case 96:
							goto IL_0699;
						case 97:
							goto IL_06ac;
						case 98:
							goto IL_06bf;
						case 99:
							goto IL_06d2;
						case 100:
							goto IL_06e5;
						case 101:
							goto IL_06f8;
						case 102:
							goto IL_070b;
						case 103:
							goto IL_071e;
						case 104:
							goto IL_0731;
						case 105:
							goto IL_0744;
						case 106:
							goto IL_0757;
						case 107:
							goto IL_076a;
						case 108:
							goto IL_077d;
						case 109:
							goto IL_0790;
						case 110:
							goto IL_07a3;
						case 111:
							goto IL_07b6;
						case 112:
							goto IL_07c9;
						case 113:
							goto IL_07dc;
						case 114:
							goto IL_07ef;
						case 115:
							goto IL_0802;
						case 116:
							goto IL_0815;
						case 117:
							goto IL_0828;
						case 118:
							goto IL_083b;
						case 119:
							goto IL_084e;
						case 120:
							goto IL_0861;
						case 121:
							goto IL_0874;
						case 122:
							goto IL_0887;
						case 123:
							goto IL_089a;
						case 124:
							goto IL_08ad;
						case 125:
							goto IL_08c0;
						case 126:
							goto IL_08d3;
						case 127:
							goto IL_08e6;
						case 128:
							goto IL_08f9;
						case 129:
							goto IL_090f;
						case 130:
							goto IL_0925;
						case 131:
							goto IL_093b;
						case 132:
							goto IL_0951;
						case 133:
							goto IL_0967;
						case 134:
							goto IL_097d;
						case 135:
							goto IL_0993;
						case 136:
							goto IL_09a9;
						case 137:
							goto IL_09bf;
						case 138:
							goto IL_09d5;
						case 139:
							goto IL_09eb;
						case 140:
							goto IL_0a01;
						case 141:
							goto IL_0a17;
						case 142:
							goto IL_0a2d;
						case 143:
							goto IL_0a43;
						case 144:
							goto IL_0a59;
						case 145:
							goto IL_0a6f;
						case 146:
							goto IL_0a85;
						case 147:
							goto IL_0a9b;
						case 148:
							goto IL_0ab1;
						case 149:
							goto IL_0ac7;
						case 150:
							goto IL_0add;
						case 151:
							goto IL_0af3;
						case 152:
							goto IL_0b09;
						case 153:
							goto IL_0b1f;
						case 154:
							goto IL_0b35;
						case 155:
							goto IL_0b4b;
						case 156:
							goto IL_0b61;
						case 157:
							goto IL_0b77;
						case 158:
							goto IL_0b8d;
						case 159:
							goto IL_0ba3;
						case 160:
							goto IL_0bb9;
						case 161:
							goto IL_0bcf;
						case 162:
							goto IL_0be5;
						case 163:
							goto IL_0bfb;
						case 164:
							goto IL_0c11;
						case 165:
							goto IL_0c27;
						case 166:
							goto IL_0c3d;
						case 167:
							goto IL_0c53;
						case 168:
							goto IL_0c69;
						case 169:
							goto IL_0c7f;
						case 170:
							goto IL_0c95;
						case 171:
							goto IL_0cab;
						case 172:
							goto IL_0cc1;
						case 173:
							goto IL_0cd7;
						case 174:
							goto IL_0ced;
						case 175:
							goto IL_0d03;
						case 176:
							goto IL_0d19;
						case 177:
							goto IL_0d2f;
						case 178:
							goto IL_0d45;
						case 179:
							goto IL_0d5b;
						case 180:
							goto IL_0d71;
						case 181:
							goto IL_0d87;
						case 182:
							goto IL_0d9d;
						case 183:
							goto IL_0db3;
						case 184:
							goto IL_0dc9;
						case 185:
							goto IL_0ddf;
						case 186:
							goto IL_0df5;
						case 187:
							goto IL_0e0b;
						case 188:
							goto IL_0e21;
						case 189:
							goto IL_0e37;
						case 190:
							goto IL_0e4d;
						case 191:
							goto IL_0e63;
						case 192:
							goto IL_0e79;
						case 193:
							goto IL_0e8f;
						case 194:
							goto IL_0ea5;
						case 195:
							goto IL_0ebb;
						case 196:
							goto IL_0ed1;
						case 197:
							goto IL_0ee7;
						case 198:
							goto IL_0efd;
						case 199:
							goto IL_0f13;
						case 200:
							goto IL_0f29;
						case 201:
							goto IL_0f3f;
						case 202:
							goto IL_0f55;
						case 203:
							goto IL_0f6b;
						case 204:
							goto IL_0f81;
						case 205:
							goto IL_0f97;
						case 206:
							goto IL_0fad;
						case 207:
							goto IL_0fc3;
						case 208:
							goto IL_0fd9;
						case 209:
							goto IL_0fef;
						case 210:
							goto IL_1005;
						case 211:
							goto IL_101b;
						case 212:
						case 213:
							goto end_IL_0000_2;
						default:
							goto end_IL_0000;
						case 214:
							goto end_IL_0000_3;
						}
						goto default;
					}
					IL_101b:
					num2 = 211;
					GetFromCollection(collection, ref n, ref codes.EOVMillingOuter);
					break;
					IL_0007:
					num2 = 2;
					codesFilePath = GetCodesFilePath();
					goto IL_0010;
					IL_0010:
					num2 = 3;
					collection = new Collection();
					goto IL_0019;
					IL_0019:
					num2 = 4;
					n = 0;
					goto IL_001d;
					IL_001d:
					num2 = 5;
					if (Strings.Len(Microsoft.VisualBasic.FileSystem.Dir(codesFilePath)) != 0)
					{
						goto IL_0031;
					}
					goto IL_00d6;
					IL_0031:
					num2 = 6;
					textFieldParser = new TextFieldParser(codesFilePath, Encoding.Default);
					goto IL_0041;
					IL_0041:
					num2 = 7;
					textFieldParser.SetDelimiters(",");
					goto IL_00c8;
					IL_00c8:
					num2 = 9;
					if (!textFieldParser.EndOfData)
					{
						goto IL_005a;
					}
					goto IL_00e0;
					IL_005a:
					num2 = 10;
					text = textFieldParser.ReadLine();
					goto IL_0066;
					IL_0066:
					num2 = 11;
					if (text.IndexOf(",", StringComparison.Ordinal) != -1)
					{
						goto IL_0079;
					}
					goto IL_00c8;
					IL_0079:
					num2 = 12;
					text2 = text.Substring(0, text.IndexOf(",", StringComparison.Ordinal));
					goto IL_0093;
					IL_0093:
					num2 = 13;
					item = text.Substring(checked(text.IndexOf(",", StringComparison.Ordinal) + 1));
					goto IL_00ae;
					IL_00ae:
					num2 = 14;
					collection.Add(item, "I" + text2);
					goto IL_00c8;
					IL_00d6:
					num2 = 17;
					FillDefaults(collection);
					goto IL_00e0;
					IL_00e0:
					num2 = 18;
					codes = Codes;
					goto IL_00ea;
					IL_00ea:
					num2 = 19;
					codes.CodesStructureFilled = true;
					goto IL_00f5;
					IL_00f5:
					num2 = 20;
					GetFromCollection(collection, ref n, ref codes.Crown);
					goto IL_0108;
					IL_0108:
					num2 = 21;
					GetFromCollection(collection, ref n, ref codes.CrownPave1);
					goto IL_011b;
					IL_011b:
					num2 = 22;
					GetFromCollection(collection, ref n, ref codes.CrownPave2);
					goto IL_012e;
					IL_012e:
					num2 = 23;
					GetFromCollection(collection, ref n, ref codes.CrownBase);
					goto IL_0141;
					IL_0141:
					num2 = 24;
					GetFromCollection(collection, ref n, ref codes.CrownSub);
					goto IL_0154;
					IL_0154:
					num2 = 25;
					GetFromCollection(collection, ref n, ref codes.ETW);
					goto IL_0167;
					IL_0167:
					num2 = 26;
					GetFromCollection(collection, ref n, ref codes.ETWPave1);
					goto IL_017a;
					IL_017a:
					num2 = 27;
					GetFromCollection(collection, ref n, ref codes.ETWPave2);
					goto IL_018d;
					IL_018d:
					num2 = 28;
					GetFromCollection(collection, ref n, ref codes.ETWBase);
					goto IL_01a0;
					IL_01a0:
					num2 = 29;
					GetFromCollection(collection, ref n, ref codes.ETWSub);
					goto IL_01b3;
					IL_01b3:
					num2 = 30;
					GetFromCollection(collection, ref n, ref codes.Lane);
					goto IL_01c6;
					IL_01c6:
					num2 = 31;
					GetFromCollection(collection, ref n, ref codes.LanePave1);
					goto IL_01d9;
					IL_01d9:
					num2 = 32;
					GetFromCollection(collection, ref n, ref codes.LanePave2);
					goto IL_01ec;
					IL_01ec:
					num2 = 33;
					GetFromCollection(collection, ref n, ref codes.LaneBase);
					goto IL_01ff;
					IL_01ff:
					num2 = 34;
					GetFromCollection(collection, ref n, ref codes.LaneSub);
					goto IL_0212;
					IL_0212:
					num2 = 35;
					GetFromCollection(collection, ref n, ref codes.EPS);
					goto IL_0225;
					IL_0225:
					num2 = 36;
					GetFromCollection(collection, ref n, ref codes.EPSPave1);
					goto IL_0238;
					IL_0238:
					num2 = 37;
					GetFromCollection(collection, ref n, ref codes.EPSPave2);
					goto IL_024b;
					IL_024b:
					num2 = 38;
					GetFromCollection(collection, ref n, ref codes.EPSBase);
					goto IL_025e;
					IL_025e:
					num2 = 39;
					GetFromCollection(collection, ref n, ref codes.EPSSub);
					goto IL_0271;
					IL_0271:
					num2 = 40;
					GetFromCollection(collection, ref n, ref codes.EPSBaseIn);
					goto IL_0284;
					IL_0284:
					num2 = 41;
					GetFromCollection(collection, ref n, ref codes.EPSSubIn);
					goto IL_0297;
					IL_0297:
					num2 = 42;
					GetFromCollection(collection, ref n, ref codes.ESUnpaved);
					goto IL_02aa;
					IL_02aa:
					num2 = 43;
					GetFromCollection(collection, ref n, ref codes.DaylightSub);
					goto IL_02bd;
					IL_02bd:
					num2 = 44;
					GetFromCollection(collection, ref n, ref codes.Daylight);
					goto IL_02d0;
					IL_02d0:
					num2 = 45;
					GetFromCollection(collection, ref n, ref codes.DaylightFill);
					goto IL_02e3;
					IL_02e3:
					num2 = 46;
					GetFromCollection(collection, ref n, ref codes.DaylightCut);
					goto IL_02f6;
					IL_02f6:
					num2 = 47;
					GetFromCollection(collection, ref n, ref codes.DitchIn);
					goto IL_0309;
					IL_0309:
					num2 = 48;
					GetFromCollection(collection, ref n, ref codes.DitchOut);
					goto IL_031c;
					IL_031c:
					num2 = 49;
					GetFromCollection(collection, ref n, ref codes.BenchIn);
					goto IL_032f;
					IL_032f:
					num2 = 50;
					GetFromCollection(collection, ref n, ref codes.BenchOut);
					goto IL_0342;
					IL_0342:
					num2 = 51;
					GetFromCollection(collection, ref n, ref codes.FlowlineDitch);
					goto IL_0355;
					IL_0355:
					num2 = 52;
					GetFromCollection(collection, ref n, ref codes.LMedDitch);
					goto IL_0368;
					IL_0368:
					num2 = 53;
					GetFromCollection(collection, ref n, ref codes.RMedDitch);
					goto IL_037b;
					IL_037b:
					num2 = 54;
					GetFromCollection(collection, ref n, ref codes.Flange);
					goto IL_038e;
					IL_038e:
					num2 = 55;
					GetFromCollection(collection, ref n, ref codes.Flowline_Gutter);
					goto IL_03a1;
					IL_03a1:
					num2 = 56;
					GetFromCollection(collection, ref n, ref codes.TopCurb);
					goto IL_03b4;
					IL_03b4:
					num2 = 57;
					GetFromCollection(collection, ref n, ref codes.BottomCurb);
					goto IL_03c7;
					IL_03c7:
					num2 = 58;
					GetFromCollection(collection, ref n, ref codes.BackCurb);
					goto IL_03da;
					IL_03da:
					num2 = 59;
					GetFromCollection(collection, ref n, ref codes.SidewalkIn);
					goto IL_03ed;
					IL_03ed:
					num2 = 60;
					GetFromCollection(collection, ref n, ref codes.SidewalkOut);
					goto IL_0400;
					IL_0400:
					num2 = 61;
					GetFromCollection(collection, ref n, ref codes.HingeCut);
					goto IL_0413;
					IL_0413:
					num2 = 62;
					GetFromCollection(collection, ref n, ref codes.HingeFill);
					goto IL_0426;
					IL_0426:
					num2 = 63;
					GetFromCollection(collection, ref n, ref codes.Top);
					goto IL_0439;
					IL_0439:
					num2 = 64;
					GetFromCollection(collection, ref n, ref codes.Datum);
					goto IL_044c;
					IL_044c:
					num2 = 65;
					GetFromCollection(collection, ref n, ref codes.Pave);
					goto IL_045f;
					IL_045f:
					num2 = 66;
					GetFromCollection(collection, ref n, ref codes.Pave1);
					goto IL_0472;
					IL_0472:
					num2 = 67;
					GetFromCollection(collection, ref n, ref codes.Pave2);
					goto IL_0485;
					IL_0485:
					num2 = 68;
					GetFromCollection(collection, ref n, ref codes.Base);
					goto IL_0498;
					IL_0498:
					num2 = 69;
					GetFromCollection(collection, ref n, ref codes.SubBase);
					goto IL_04ab;
					IL_04ab:
					num2 = 70;
					GetFromCollection(collection, ref n, ref codes.Gravel);
					goto IL_04be;
					IL_04be:
					num2 = 71;
					GetFromCollection(collection, ref n, ref codes.TopCurbNew);
					goto IL_04d1;
					IL_04d1:
					num2 = 72;
					GetFromCollection(collection, ref n, ref codes.BackCurbNew);
					goto IL_04e4;
					IL_04e4:
					num2 = 73;
					GetFromCollection(collection, ref n, ref codes.Curb);
					goto IL_04f7;
					IL_04f7:
					num2 = 74;
					GetFromCollection(collection, ref n, ref codes.Sidewalk);
					goto IL_050a;
					IL_050a:
					num2 = 75;
					GetFromCollection(collection, ref n, ref codes.Hinge);
					goto IL_051d;
					IL_051d:
					num2 = 76;
					GetFromCollection(collection, ref n, ref codes.EOV);
					goto IL_0530;
					IL_0530:
					num2 = 77;
					GetFromCollection(collection, ref n, ref codes.EOVOverlay);
					goto IL_0543;
					IL_0543:
					num2 = 78;
					GetFromCollection(collection, ref n, ref codes.Level);
					goto IL_0556;
					IL_0556:
					num2 = 79;
					GetFromCollection(collection, ref n, ref codes.Mill);
					goto IL_0569;
					IL_0569:
					num2 = 80;
					GetFromCollection(collection, ref n, ref codes.Overlay);
					goto IL_057c;
					IL_057c:
					num2 = 81;
					GetFromCollection(collection, ref n, ref codes.CrownOverlay);
					goto IL_058f;
					IL_058f:
					num2 = 82;
					GetFromCollection(collection, ref n, ref codes.Barrier);
					goto IL_05a2;
					IL_05a2:
					num2 = 83;
					GetFromCollection(collection, ref n, ref codes.EBD);
					goto IL_05b5;
					IL_05b5:
					num2 = 84;
					GetFromCollection(collection, ref n, ref codes.CrownDeck);
					goto IL_05c8;
					IL_05c8:
					num2 = 85;
					GetFromCollection(collection, ref n, ref codes.Deck);
					goto IL_05db;
					IL_05db:
					num2 = 86;
					GetFromCollection(collection, ref n, ref codes.Girder);
					goto IL_05ee;
					IL_05ee:
					num2 = 87;
					GetFromCollection(collection, ref n, ref codes.EBS);
					goto IL_0601;
					IL_0601:
					num2 = 88;
					GetFromCollection(collection, ref n, ref codes.ESL);
					goto IL_0614;
					IL_0614:
					num2 = 89;
					GetFromCollection(collection, ref n, ref codes.DaylightBallast);
					goto IL_0627;
					IL_0627:
					num2 = 90;
					GetFromCollection(collection, ref n, ref codes.ESBS);
					goto IL_063a;
					IL_063a:
					num2 = 91;
					GetFromCollection(collection, ref n, ref codes.DaylightSubballast);
					goto IL_064d;
					IL_064d:
					num2 = 92;
					GetFromCollection(collection, ref n, ref codes.Ballast);
					goto IL_0660;
					IL_0660:
					num2 = 93;
					GetFromCollection(collection, ref n, ref codes.Sleeper);
					goto IL_0673;
					IL_0673:
					num2 = 94;
					GetFromCollection(collection, ref n, ref codes.Subballast);
					goto IL_0686;
					IL_0686:
					num2 = 95;
					GetFromCollection(collection, ref n, ref codes.Rail);
					goto IL_0699;
					IL_0699:
					num2 = 96;
					GetFromCollection(collection, ref n, ref codes.R1);
					goto IL_06ac;
					IL_06ac:
					num2 = 97;
					GetFromCollection(collection, ref n, ref codes.R2);
					goto IL_06bf;
					IL_06bf:
					num2 = 98;
					GetFromCollection(collection, ref n, ref codes.R3);
					goto IL_06d2;
					IL_06d2:
					num2 = 99;
					GetFromCollection(collection, ref n, ref codes.R4);
					goto IL_06e5;
					IL_06e5:
					num2 = 100;
					GetFromCollection(collection, ref n, ref codes.R5);
					goto IL_06f8;
					IL_06f8:
					num2 = 101;
					GetFromCollection(collection, ref n, ref codes.R6);
					goto IL_070b;
					IL_070b:
					num2 = 102;
					GetFromCollection(collection, ref n, ref codes.Bridge);
					goto IL_071e;
					IL_071e:
					num2 = 103;
					GetFromCollection(collection, ref n, ref codes.Ditch);
					goto IL_0731;
					IL_0731:
					num2 = 104;
					GetFromCollection(collection, ref n, ref codes.CrownFin);
					goto IL_0744;
					IL_0744:
					num2 = 105;
					GetFromCollection(collection, ref n, ref codes.CrownSubBase);
					goto IL_0757;
					IL_0757:
					num2 = 106;
					GetFromCollection(collection, ref n, ref codes.ETWSubBase);
					goto IL_076a;
					IL_076a:
					num2 = 107;
					GetFromCollection(collection, ref n, ref codes.MarkedPoint);
					goto IL_077d;
					IL_077d:
					num2 = 108;
					GetFromCollection(collection, ref n, ref codes.Guardrail);
					goto IL_0790;
					IL_0790:
					num2 = 109;
					GetFromCollection(collection, ref n, ref codes.Median);
					goto IL_07a3;
					IL_07a3:
					num2 = 110;
					GetFromCollection(collection, ref n, ref codes.ETWOverlay);
					goto IL_07b6;
					IL_07b6:
					num2 = 111;
					GetFromCollection(collection, ref n, ref codes.TrenchBottom);
					goto IL_07c9;
					IL_07c9:
					num2 = 112;
					GetFromCollection(collection, ref n, ref codes.TrenchDaylight);
					goto IL_07dc;
					IL_07dc:
					num2 = 113;
					GetFromCollection(collection, ref n, ref codes.TrenchBedding);
					goto IL_07ef;
					IL_07ef:
					num2 = 114;
					GetFromCollection(collection, ref n, ref codes.TrenchBackfill);
					goto IL_0802;
					IL_0802:
					num2 = 115;
					GetFromCollection(collection, ref n, ref codes.Trench);
					goto IL_0815;
					IL_0815:
					num2 = 116;
					GetFromCollection(collection, ref n, ref codes.LaneBreak);
					goto IL_0828;
					IL_0828:
					num2 = 117;
					GetFromCollection(collection, ref n, ref codes.LaneBreakOverlay);
					goto IL_083b;
					IL_083b:
					num2 = 118;
					GetFromCollection(collection, ref n, ref codes.Sod);
					goto IL_084e;
					IL_084e:
					num2 = 119;
					GetFromCollection(collection, ref n, ref codes.DaylightStrip);
					goto IL_0861;
					IL_0861:
					num2 = 120;
					GetFromCollection(collection, ref n, ref codes.sForeslopeStripping);
					goto IL_0874;
					IL_0874:
					num2 = 121;
					GetFromCollection(collection, ref n, ref codes.Stripping);
					goto IL_0887;
					IL_0887:
					num2 = 122;
					GetFromCollection(collection, ref n, ref codes.ChannelFlowline);
					goto IL_089a;
					IL_089a:
					num2 = 123;
					GetFromCollection(collection, ref n, ref codes.Channe_Bottom);
					goto IL_08ad;
					IL_08ad:
					num2 = 124;
					GetFromCollection(collection, ref n, ref codes.ChannelTop);
					goto IL_08c0;
					IL_08c0:
					num2 = 125;
					GetFromCollection(collection, ref n, ref codes.ChannelExtension);
					goto IL_08d3;
					IL_08d3:
					num2 = 126;
					GetFromCollection(collection, ref n, ref codes.ChannelBackslope);
					goto IL_08e6;
					IL_08e6:
					num2 = 127;
					GetFromCollection(collection, ref n, ref codes.LiningMaterial);
					goto IL_08f9;
					IL_08f9:
					num2 = 128;
					GetFromCollection(collection, ref n, ref codes.DitchBack);
					goto IL_090f;
					IL_090f:
					num2 = 129;
					GetFromCollection(collection, ref n, ref codes.DitchFace);
					goto IL_0925;
					IL_0925:
					num2 = 130;
					GetFromCollection(collection, ref n, ref codes.DitchTop);
					goto IL_093b;
					IL_093b:
					num2 = 131;
					GetFromCollection(collection, ref n, ref codes.DitchBottom);
					goto IL_0951;
					IL_0951:
					num2 = 132;
					GetFromCollection(collection, ref n, ref codes.Backfill);
					goto IL_0967;
					IL_0967:
					num2 = 133;
					GetFromCollection(collection, ref n, ref codes.BackfillFace);
					goto IL_097d;
					IL_097d:
					num2 = 134;
					GetFromCollection(collection, ref n, ref codes.DitchLidFace);
					goto IL_0993;
					IL_0993:
					num2 = 135;
					GetFromCollection(collection, ref n, ref codes.LidTop);
					goto IL_09a9;
					IL_09a9:
					num2 = 136;
					GetFromCollection(collection, ref n, ref codes.DitchBackFill);
					goto IL_09bf;
					IL_09bf:
					num2 = 137;
					GetFromCollection(collection, ref n, ref codes.Lid);
					goto IL_09d5;
					IL_09d5:
					num2 = 138;
					GetFromCollection(collection, ref n, ref codes.DrainBottom);
					goto IL_09eb;
					IL_09eb:
					num2 = 139;
					GetFromCollection(collection, ref n, ref codes.DrainBottomOutside);
					goto IL_0a01;
					IL_0a01:
					num2 = 140;
					GetFromCollection(collection, ref n, ref codes.DrainTopOutside);
					goto IL_0a17;
					IL_0a17:
					num2 = 141;
					GetFromCollection(collection, ref n, ref codes.DrainTopInside);
					goto IL_0a2d;
					IL_0a2d:
					num2 = 142;
					GetFromCollection(collection, ref n, ref codes.DrainBottomInside);
					goto IL_0a43;
					IL_0a43:
					num2 = 143;
					GetFromCollection(collection, ref n, ref codes.DrainCenter);
					goto IL_0a59;
					IL_0a59:
					num2 = 144;
					GetFromCollection(collection, ref n, ref codes.FlowLine);
					goto IL_0a6f;
					IL_0a6f:
					num2 = 145;
					GetFromCollection(collection, ref n, ref codes.DrainTop);
					goto IL_0a85;
					IL_0a85:
					num2 = 146;
					GetFromCollection(collection, ref n, ref codes.DrainStructure);
					goto IL_0a9b;
					IL_0a9b:
					num2 = 147;
					GetFromCollection(collection, ref n, ref codes.DrainArea);
					goto IL_0ab1;
					IL_0ab1:
					num2 = 148;
					GetFromCollection(collection, ref n, ref codes.RWFront);
					goto IL_0ac7;
					IL_0ac7:
					num2 = 149;
					GetFromCollection(collection, ref n, ref codes.RWTop);
					goto IL_0add;
					IL_0add:
					num2 = 150;
					GetFromCollection(collection, ref n, ref codes.RWBack);
					goto IL_0af3;
					IL_0af3:
					num2 = 151;
					GetFromCollection(collection, ref n, ref codes.RWHinge);
					goto IL_0b09;
					IL_0b09:
					num2 = 152;
					GetFromCollection(collection, ref n, ref codes.RWInside);
					goto IL_0b1f;
					IL_0b1f:
					num2 = 153;
					GetFromCollection(collection, ref n, ref codes.RWOutside);
					goto IL_0b35;
					IL_0b35:
					num2 = 154;
					GetFromCollection(collection, ref n, ref codes.Wall);
					goto IL_0b4b;
					IL_0b4b:
					num2 = 155;
					GetFromCollection(collection, ref n, ref codes.RWall);
					goto IL_0b61;
					IL_0b61:
					num2 = 156;
					GetFromCollection(collection, ref n, ref codes.RWallB1);
					goto IL_0b77;
					IL_0b77:
					num2 = 157;
					GetFromCollection(collection, ref n, ref codes.RWallB2);
					goto IL_0b8d;
					IL_0b8d:
					num2 = 158;
					GetFromCollection(collection, ref n, ref codes.RWallB3);
					goto IL_0ba3;
					IL_0ba3:
					num2 = 159;
					GetFromCollection(collection, ref n, ref codes.RWallB4);
					goto IL_0bb9;
					IL_0bb9:
					num2 = 160;
					GetFromCollection(collection, ref n, ref codes.RWallK1);
					goto IL_0bcf;
					IL_0bcf:
					num2 = 161;
					GetFromCollection(collection, ref n, ref codes.RWallK2);
					goto IL_0be5;
					IL_0be5:
					num2 = 162;
					GetFromCollection(collection, ref n, ref codes.FootingBottom);
					goto IL_0bfb;
					IL_0bfb:
					num2 = 163;
					GetFromCollection(collection, ref n, ref codes.WalkEdge);
					goto IL_0c11;
					IL_0c11:
					num2 = 164;
					GetFromCollection(collection, ref n, ref codes.Lot);
					goto IL_0c27;
					IL_0c27:
					num2 = 165;
					GetFromCollection(collection, ref n, ref codes.Slope_Link);
					goto IL_0c3d;
					IL_0c3d:
					num2 = 166;
					GetFromCollection(collection, ref n, ref codes.Channel_Side);
					goto IL_0c53;
					IL_0c53:
					num2 = 167;
					GetFromCollection(collection, ref n, ref codes.Bench);
					goto IL_0c69;
					IL_0c69:
					num2 = 168;
					GetFromCollection(collection, ref n, ref codes.CrownPave3);
					goto IL_0c7f;
					IL_0c7f:
					num2 = 169;
					GetFromCollection(collection, ref n, ref codes.LanePave3);
					goto IL_0c95;
					IL_0c95:
					num2 = 170;
					GetFromCollection(collection, ref n, ref codes.ETWBase1);
					goto IL_0cab;
					IL_0cab:
					num2 = 171;
					GetFromCollection(collection, ref n, ref codes.CrownBase1);
					goto IL_0cc1;
					IL_0cc1:
					num2 = 172;
					GetFromCollection(collection, ref n, ref codes.LaneBase1);
					goto IL_0cd7;
					IL_0cd7:
					num2 = 173;
					GetFromCollection(collection, ref n, ref codes.ETWBase2);
					goto IL_0ced;
					IL_0ced:
					num2 = 174;
					GetFromCollection(collection, ref n, ref codes.CrownBase2);
					goto IL_0d03;
					IL_0d03:
					num2 = 175;
					GetFromCollection(collection, ref n, ref codes.LaneBase2);
					goto IL_0d19;
					IL_0d19:
					num2 = 176;
					GetFromCollection(collection, ref n, ref codes.ETWBase3);
					goto IL_0d2f;
					IL_0d2f:
					num2 = 177;
					GetFromCollection(collection, ref n, ref codes.CrownBase3);
					goto IL_0d45;
					IL_0d45:
					num2 = 178;
					GetFromCollection(collection, ref n, ref codes.LaneBase3);
					goto IL_0d5b;
					IL_0d5b:
					num2 = 179;
					GetFromCollection(collection, ref n, ref codes.ETWSub1);
					goto IL_0d71;
					IL_0d71:
					num2 = 180;
					GetFromCollection(collection, ref n, ref codes.CrownSub1);
					goto IL_0d87;
					IL_0d87:
					num2 = 181;
					GetFromCollection(collection, ref n, ref codes.LaneSub1);
					goto IL_0d9d;
					IL_0d9d:
					num2 = 182;
					GetFromCollection(collection, ref n, ref codes.ETWSub2);
					goto IL_0db3;
					IL_0db3:
					num2 = 183;
					GetFromCollection(collection, ref n, ref codes.CrownSub2);
					goto IL_0dc9;
					IL_0dc9:
					num2 = 184;
					GetFromCollection(collection, ref n, ref codes.LaneSub2);
					goto IL_0ddf;
					IL_0ddf:
					num2 = 185;
					GetFromCollection(collection, ref n, ref codes.ETWSub3);
					goto IL_0df5;
					IL_0df5:
					num2 = 186;
					GetFromCollection(collection, ref n, ref codes.CrownSub3);
					goto IL_0e0b;
					IL_0e0b:
					num2 = 187;
					GetFromCollection(collection, ref n, ref codes.LaneSub3);
					goto IL_0e21;
					IL_0e21:
					num2 = 188;
					GetFromCollection(collection, ref n, ref codes.Pave3);
					goto IL_0e37;
					IL_0e37:
					num2 = 189;
					GetFromCollection(collection, ref n, ref codes.Base1);
					goto IL_0e4d;
					IL_0e4d:
					num2 = 190;
					GetFromCollection(collection, ref n, ref codes.Base2);
					goto IL_0e63;
					IL_0e63:
					num2 = 191;
					GetFromCollection(collection, ref n, ref codes.Base3);
					goto IL_0e79;
					IL_0e79:
					num2 = 192;
					GetFromCollection(collection, ref n, ref codes.Subbase1);
					goto IL_0e8f;
					IL_0e8f:
					num2 = 193;
					GetFromCollection(collection, ref n, ref codes.Subbase2);
					goto IL_0ea5;
					IL_0ea5:
					num2 = 194;
					GetFromCollection(collection, ref n, ref codes.Subbase3);
					goto IL_0ebb;
					IL_0ebb:
					num2 = 195;
					GetFromCollection(collection, ref n, ref codes.EPSBase1);
					goto IL_0ed1;
					IL_0ed1:
					num2 = 196;
					GetFromCollection(collection, ref n, ref codes.EPSBase2);
					goto IL_0ee7;
					IL_0ee7:
					num2 = 197;
					GetFromCollection(collection, ref n, ref codes.EPSBase3);
					goto IL_0efd;
					IL_0efd:
					num2 = 198;
					GetFromCollection(collection, ref n, ref codes.EPSSubBase1);
					goto IL_0f13;
					IL_0f13:
					num2 = 199;
					GetFromCollection(collection, ref n, ref codes.EPSSubBase2);
					goto IL_0f29;
					IL_0f29:
					num2 = 200;
					GetFromCollection(collection, ref n, ref codes.EPSSubBase3);
					goto IL_0f3f;
					IL_0f3f:
					num2 = 201;
					GetFromCollection(collection, ref n, ref codes.ETWPave3);
					goto IL_0f55;
					IL_0f55:
					num2 = 202;
					GetFromCollection(collection, ref n, ref codes.EPSBase4);
					goto IL_0f6b;
					IL_0f6b:
					num2 = 203;
					GetFromCollection(collection, ref n, ref codes.Base4);
					goto IL_0f81;
					IL_0f81:
					num2 = 204;
					GetFromCollection(collection, ref n, ref codes.SR);
					goto IL_0f97;
					IL_0f97:
					num2 = 205;
					GetFromCollection(collection, ref n, ref codes.EPSPave3);
					goto IL_0fad;
					IL_0fad:
					num2 = 206;
					GetFromCollection(collection, ref n, ref codes.EOVMilling);
					goto IL_0fc3;
					IL_0fc3:
					num2 = 207;
					GetFromCollection(collection, ref n, ref codes.EOVLeveling);
					goto IL_0fd9;
					IL_0fd9:
					num2 = 208;
					GetFromCollection(collection, ref n, ref codes.CrownLeveling);
					goto IL_0fef;
					IL_0fef:
					num2 = 209;
					GetFromCollection(collection, ref n, ref codes.CrownMilling);
					goto IL_1005;
					IL_1005:
					num2 = 210;
					GetFromCollection(collection, ref n, ref codes.EOVMillingInner);
					goto IL_101b;
					end_IL_0000_2:
					break;
				}
				num2 = 213;
				collection = null;
				break;
				end_IL_0000:;
			}
            catch (Exception ex)
            {
                ProjectData.SetProjectError(ex);
                try0000_dispatch = 2720;   // se essa variável existir no método
                                           // se não usar mais esse dispatcher, pode simplesmente omitir essa linha
            }
            throw ProjectData.CreateProjectError(-2146828237);
			continue;
			end_IL_0000_3:
			break;
		}
		if (num != 0)
		{
			ProjectData.ClearProjectError();
		}
	}

	private static void GetFromCollection(Collection colCodesAndDescriptionHashtable, ref int n, ref CodeType g_sEachCode)
	{
		int try0000_dispatch = -1;
		int num2 = default(int);
		int num = default(int);
		while (true)
		{
			try
			{
				/*Note: ILSpy has introduced the following switch to emulate a goto from catch-block to try-block*/;
				checked
				{
					switch (try0000_dispatch)
					{
					default:
					{
						ProjectData.ClearProjectError();
						num2 = 2;
						n++;
						g_sEachCode.Index = n;
						string text = Conversions.ToString(colCodesAndDescriptionHashtable["I" + Conversions.ToString(n)]);
						int num3 = Strings.InStr(1, text, ",");
						string code;
						string description;
						if (num3 != 0)
						{
							code = Strings.Left(text, num3 - 1);
							int num4 = Strings.InStr(num3 + 1, text, ",");
							description = ((num4 == 0) ? "" : Strings.Mid(text, num4 + 1));
						}
						else
						{
							code = text;
							description = "";
						}
						g_sEachCode.Code = code;
						g_sEachCode.Description = description;
						break;
					}
					case 164:
						num = -1;
						switch (num2)
						{
						case 2:
							break;
						default:
							goto end_IL_0000;
						}
						break;
					}
					_ = Information.Err().Number;
					break;
				}
				end_IL_0000:;
			}
            catch (Exception ex)
            {
                ProjectData.SetProjectError(ex);
                try0000_dispatch = 2720;   // se essa variável existir no método
                                           // se não usar mais esse dispatcher, pode simplesmente omitir essa linha
            }
            throw ProjectData.CreateProjectError(-2146828237);
		}
		if (num != 0)
		{
			ProjectData.ClearProjectError();
		}
	}
}
