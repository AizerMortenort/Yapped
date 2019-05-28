﻿using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CellType = SoulsFormats.PARAM64.CellType;

namespace Yapped
{
    internal static class Util
    {
        public static Dictionary<string, PARAM64.Layout> LoadLayouts(string directory)
        {
            var layouts = new Dictionary<string, PARAM64.Layout>();
            if (Directory.Exists(directory))
            {
                foreach (string path in Directory.GetFiles(directory, "*.xml"))
                {
                    string paramID = Path.GetFileNameWithoutExtension(path);
                    try
                    {
                        PARAM64.Layout layout = PARAM64.Layout.ReadXMLFile(path);
                        layouts[paramID] = layout;
                    }
                    catch (Exception ex)
                    {
                        ShowError($"Failed to load layout {paramID}.txt\r\n\r\n{ex}");
                    }
                }
            }
            return layouts;
        }

        public static LoadParamsResult LoadParams(string paramPath, Dictionary<string, ParamInfo> paramInfo,
            Dictionary<string, PARAM64.Layout> layouts, GameMode gameMode, bool hideUnusedParams)
        {
            if (!File.Exists(paramPath))
            {
                ShowError($"Parambnd not found:\r\n{paramPath}\r\nPlease browse to the Data0.bdt or parambnd you would like to edit.");
                return null;
            }

            var result = new LoadParamsResult();
            try
            {
                if (BND4.Is(paramPath))
                {
                    result.ParamBND = BND4.Read(paramPath);
                    result.Encrypted = false;
                }
                else if (BND3.Is(paramPath))
                {
                    result.ParamBND = BND3.Read(paramPath);
                    result.Encrypted = false;
                }
                else if (gameMode.Game == GameMode.GameType.DarkSouls2)
                {
                    result.ParamBND = DecryptDS2Regulation(paramPath);
                    result.Encrypted = true;
                }
                else if (gameMode.Game == GameMode.GameType.DarkSouls3)
                {
                    result.ParamBND = SFUtil.DecryptDS3Regulation(paramPath);
                    result.Encrypted = true;
                }
                else
                {
                    throw new FormatException("Unrecognized file format.");
                }
            }
            catch (DllNotFoundException ex) when (ex.Message.Contains("oo2core_6_win64.dll"))
            {
                ShowError("In order to load Sekiro params, you must copy oo2core_6_win64.dll from Sekiro into Yapped's lib folder.");
                return null;
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load parambnd:\r\n{paramPath}\r\n\r\n{ex}");
                return null;
            }

            result.ParamWrappers = new List<ParamWrapper>();
            foreach (BinderFile file in result.ParamBND.Files.Where(f => f.Name.EndsWith(".param")))
            {
                string name = Path.GetFileNameWithoutExtension(file.Name);
                if (paramInfo.ContainsKey(name))
                {
                    if (paramInfo[name].Blocked || paramInfo[name].Hidden && hideUnusedParams)
                        continue;
                }

                try
                {
                    PARAM64 param = PARAM64.Read(file.Bytes);
                    PARAM64.Layout layout = null;
                    if (layouts.ContainsKey(param.ID))
                    {
                        layout = layouts[param.ID];
                    }
                    if (layout == null || layout.Size != param.DetectedSize)
                    {
                        layout = new PARAM64.Layout();
                        layout.Add(new PARAM64.Layout.Entry(CellType.dummy8, "Unknown", (int)param.DetectedSize, null));
                    }

                    string description = null;
                    if (paramInfo.ContainsKey(name))
                        description = paramInfo[name].Description;

                    var wrapper = new ParamWrapper(name, param, layout, description);
                    result.ParamWrappers.Add(wrapper);
                }
                catch (Exception ex)
                {
                    ShowError($"Failed to load param file: {name}.param\r\n\r\n{ex}");
                }
            }

            result.ParamWrappers.Sort();
            return result;
        }

        public static string ValidateCell(CellType type, string text)
        {
            if (type == CellType.s8)
            {
                if (!sbyte.TryParse(text, out _))
                {
                    return "Invalid value for signed byte.";
                }
            }
            else if (type == CellType.u8)
            {
                if (!byte.TryParse(text, out _))
                {
                    return "Invalid value for unsigned byte.";
                }
            }
            else if (type == CellType.x8)
            {
                try
                {
                    Convert.ToByte(text, 16);
                }
                catch
                {
                    return "Invalid value for hex byte.";
                }
            }
            else if (type == CellType.s16)
            {
                if (!short.TryParse(text, out _))
                {
                    return "Invalid value for signed short.";
                }
            }
            else if (type == CellType.u16)
            {
                if (!ushort.TryParse(text, out _))
                {
                    return "Invalid value for unsigned short.";
                }
            }
            else if (type == CellType.x16)
            {
                try
                {
                    Convert.ToUInt16(text, 16);
                }
                catch
                {
                    return "Invalid value for hex short.";
                }
            }
            else if (type == CellType.s32)
            {
                if (!int.TryParse(text, out _))
                {
                    return "Invalid value for signed int.";
                }
            }
            else if (type == CellType.u32)
            {
                if (!uint.TryParse(text, out _))
                {
                    return "Invalid value for unsigned int.";
                }
            }
            else if (type == CellType.x32)
            {
                try
                {
                    Convert.ToUInt32(text, 16);
                }
                catch
                {
                    return "Invalid value for hex int.";
                }
            }
            else if (type == CellType.f32)
            {
                if (!float.TryParse(text, out _))
                {
                    return "Invalid value for float.";
                }
            }
            else if (type == CellType.b8 || type == CellType.b32)
            {
                if (!bool.TryParse(text, out _))
                {
                    return "Invalid value for bool.";
                }
            }
            else if (type == CellType.fixstr || type == CellType.fixstrW)
            {
                // Don't see how you could mess this up
            }
            else
                throw new NotImplementedException("Cannot validate cell type.");

            return null;
        }

        private static byte[] ds2RegulationKey = {
            0x40, 0x17, 0x81, 0x30, 0xDF, 0x0A, 0x94, 0x54, 0x33, 0x09, 0xE1, 0x71, 0xEC, 0xBF, 0x25, 0x4C };

        public static BND4 DecryptDS2Regulation(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] iv = new byte[16];
            iv[0] = 0x80;
            Array.Copy(bytes, 0, iv, 1, 11);
            iv[15] = 1;
            byte[] input = new byte[bytes.Length - 32];
            Array.Copy(bytes, 32, input, 0, bytes.Length - 32);
            using (var ms = new MemoryStream(input))
            {
                byte[] decrypted = CryptographyUtility.DecryptAesCtr(ms, ds2RegulationKey, iv);
                File.WriteAllBytes("ffff.bnd", decrypted);
                return BND4.Read(decrypted);
            }
        }

        public static void EncryptDS2Regulation(string path, BND4 bnd)
        {
            //var rand = new Random();
            //byte[] iv = new byte[16];
            //byte[] ivPart = new byte[11];
            //rand.NextBytes(ivPart);
            //iv[0] = 0x80;
            //Array.Copy(ivPart, 0, iv, 1, 11);
            //iv[15] = 1;
            //byte[] decrypted = bnd.Write();
            //byte[] encrypted = CryptographyUtility.EncryptAesCtr(decrypted, ds2RegulationKey, iv);
            //byte[] output = new byte[encrypted.Length + 11];

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bnd.Write(path); // xddddd
        }

        public static void ShowError(string message)
        {
            MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal class LoadParamsResult
    {
        public bool Encrypted { get; set; }

        public IBinder ParamBND { get; set; }

        public List<ParamWrapper> ParamWrappers { get; set; }
    }
}
