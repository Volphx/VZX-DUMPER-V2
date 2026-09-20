using System;
using System.IO;
using System.Text;

namespace Il2CppDumper
{
    public static class FreeFireOffsetGenerator
    {
        public static string Generate(Metadata metadata, Il2Cpp il2Cpp, string outputDir)
        {
            var is64Bit = !il2Cpp.Is32Bit;

            // 1. Resolução dinâmica de InitBase e GameFacade
            ulong initBase = il2Cpp.CodeRegistration;
            ulong gameFacade = il2Cpp.MetadataRegistration;

            if (initBase == 0)
            {
                initBase = is64Bit ? 0xA41C8B8UL : 0xA342EFCUL;
            }
            if (gameFacade == 0)
            {
                gameFacade = is64Bit ? 0xA5C7F60UL : 0xA5BC344UL;
            }

            var sb = new StringBuilder();
            sb.AppendLine("### 🌐 System & Base");
            sb.AppendLine("```ini");
            sb.AppendLine($"InitBase                    = 0x{initBase:X}");
            sb.AppendLine($"GameFacade_ScriptAddress    = 0x{gameFacade:X}");
            sb.AppendLine("```\n");

            sb.AppendLine("### 🎮 Match & Game State");
            sb.AppendLine("```ini");
            sb.AppendLine("CurrentMatchGame            = 0x4");
            sb.AppendLine("Camera_Intptr               = 0x8");
            sb.AppendLine("GameTimer                   = 0x10");
            sb.AppendLine("FixedDeltaTime              = 0x24");
            sb.AppendLine("m_Match                     = 0x50");
            sb.AppendLine("Access/StaticClass          = 0x5C");
            sb.AppendLine("DictionaryEntities          = 0x68");
            sb.AppendLine("MatchState                  = 0x8C");
            sb.AppendLine("```\n");

            sb.AppendLine("### 👤 Player & Entities");
            sb.AppendLine("```ini");
            sb.AppendLine("Level                       = 0x14");
            sb.AppendLine("ObserverPlayer              = 0x28");
            sb.AppendLine("Player_Data/Pool            = 0x48");
            sb.AppendLine("Player_IsDead               = 0x50");
            sb.AppendLine("LocalPlayer                 = 0x94");
            sb.AppendLine("CurrentObserver             = 0xB4");
            sb.AppendLine("Player_Name                 = 0x31C");
            sb.AppendLine("IsClientBot                 = 0x324");
            sb.AppendLine("LocalPlayerAttributes       = 0x500");
            sb.AppendLine("Player_ShadowBase           = 0x1A60");
            sb.AppendLine("```\n");

            sb.AppendLine("### 🎭 Avatar Properties");
            sb.AppendLine("```ini");
            sb.AppendLine("Avatar_Data                 = 0x14");
            sb.AppendLine("Avatar_Data_IsTeam          = 0x59");
            sb.AppendLine("Avatar_IsVisible            = 0x95");
            sb.AppendLine("Avatar                      = 0xA8");
            sb.AppendLine("AvatarManager               = 0x504");
            sb.AppendLine("```\n");

            sb.AppendLine("### 📷 Camera");
            sb.AppendLine("```ini");
            sb.AppendLine("Camera                      = 0x18");
            sb.AppendLine("MainCameraTransform         = 0x28C");
            sb.AppendLine("FollowCamera                = 0x494");
            sb.AppendLine("```\n");

            sb.AppendLine("### 🔫 Weapon & Combat");
            sb.AppendLine("```ini");
            sb.AppendLine("WeaponRecoil/ScatterRate    = 0xC");
            sb.AppendLine("WeaponData                  = 0x58");
            sb.AppendLine("WeaponInfo/Type             = 0x64");
            sb.AppendLine("NoReloadPtr                 = 0xC1");
            sb.AppendLine("RapidfirePtr                = 0x1C8");
            sb.AppendLine("Weapon                      = 0x434");
            sb.AppendLine("```\n");

            sb.AppendLine("### 🎯 Aim & Rotation");
            sb.AppendLine("```ini");
            sb.AppendLine("sAim4                       = 0x2C");
            sb.AppendLine("sAim3                       = 0x38");
            sb.AppendLine("AimRotation                 = 0x440");
            sb.AppendLine("AimbotVisible               = 0x4E8");
            sb.AppendLine("saim1                       = 0x58C");
            sb.AppendLine("saim2                       = 0x9D0");
            sb.AppendLine("```\n");

            sb.AppendLine("### 🦴 Player Bones / Skeleton");
            sb.AppendLine("```ini");
            sb.AppendLine("Player.RightHand            = 0x498");
            sb.AppendLine("Player.Head                 = 0x49C");
            sb.AppendLine("Player.Hip                  = 0x4A0");
            sb.AppendLine("Player.Spine                = 0x4A4");
            sb.AppendLine("Player.Groin                = 0x4AC");
            sb.AppendLine("Player.Root/Pes             = 0x4B0");
            sb.AppendLine("Player.LeftAnkle            = 0x4B8");
            sb.AppendLine("Player.RightAnkle           = 0x4BC");
            sb.AppendLine("Player.LeftToe/Foot         = 0x4C0");
            sb.AppendLine("Player.RightToe/Foot        = 0x4C4");
            sb.AppendLine("Player.LeftHand             = 0x4C8");
            sb.AppendLine("Player.LeftShoulder         = 0x4D0");
            sb.AppendLine("Player.RightShoulder        = 0x4D4");
            sb.AppendLine("Player.RightWrist           = 0x4D8");
            sb.AppendLine("Player.LeftWrist            = 0x4DC");
            sb.AppendLine("Player.RightForeArm/Elbow   = 0x4E0");
            sb.AppendLine("Player.LeftForeArm/Elbow    = 0x4E4");
            sb.AppendLine("```");

            var content = sb.ToString();

            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                try
                {
                    Directory.CreateDirectory(outputDir);
                    File.WriteAllText(Path.Combine(outputDir, "Offsets.ini"), content, Encoding.UTF8);
                    File.WriteAllText(Path.Combine(outputDir, "FreeFire_Offsets.ini"), content, Encoding.UTF8);
                    File.WriteAllText(Path.Combine(outputDir, "Offsets.txt"), content, Encoding.UTF8);
                }
                catch { }
            }

            return content;
        }
    }
}

