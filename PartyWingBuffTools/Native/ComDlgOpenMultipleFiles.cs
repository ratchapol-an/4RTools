using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PartyWingBuffTools.Native;

/// <summary>
/// Win32 <c>GetOpenFileName</c> with multiselect — reliable for unpackaged WinUI where WinRT
/// <c>FileOpenPicker</c> often returns nothing or fails to parent correctly.
/// </summary>
internal static class ComDlgOpenMultipleFiles
{
    private const int OFN_ALLOWMULTISELECT = 0x00000200;
    private const int OFN_EXPLORER = 0x00080000;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetOpenFileNameW", SetLastError = true)]
    private static extern bool GetOpenFileName(ref OpenFileNameW lpofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileNameW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public IntPtr lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public IntPtr lpstrInitialDir;
        public IntPtr lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public IntPtr lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    /// <summary>Shows the system open dialog; returns selected file paths, or empty if cancelled.</summary>
    public static IReadOnlyList<string> ShowOpenJson(IntPtr ownerHwnd)
    {
        const int bufferCharCount = 32768;
        int bufferBytes = bufferCharCount * 2;

        IntPtr fileBuffer = Marshal.AllocHGlobal(bufferBytes);
        IntPtr filterMem = Marshal.StringToHGlobalUni(
            "JSON (*.json)\0*.json\0All files (*.*)\0*.*\0\0");
        IntPtr titleMem = Marshal.StringToHGlobalUni("Import profiles");

        try
        {
            ClearBuffer(fileBuffer, bufferBytes);

            var ofn = new OpenFileNameW
            {
                lStructSize = Marshal.SizeOf<OpenFileNameW>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = filterMem,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = bufferCharCount,
                lpstrTitle = titleMem,
                Flags = OFN_ALLOWMULTISELECT | OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST,
            };

            if (!GetOpenFileName(ref ofn))
            {
                return Array.Empty<string>();
            }

            byte[] raw = new byte[bufferBytes];
            Marshal.Copy(fileBuffer, raw, 0, bufferBytes);

            int usedBytes = GetUsedUtf16ByteLength(raw);
            if (usedBytes <= 0)
            {
                return Array.Empty<string>();
            }

            string decoded = Encoding.Unicode.GetString(raw, 0, usedBytes);
            return ParseMultiSelectResult(decoded);
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
            Marshal.FreeHGlobal(filterMem);
            Marshal.FreeHGlobal(titleMem);
        }
    }

    private static void ClearBuffer(IntPtr fileBuffer, int byteLen)
    {
        byte[] zero = new byte[byteLen];
        Marshal.Copy(zero, 0, fileBuffer, byteLen);
    }

    /// <summary>Trim trailing zeros from the buffer; keep interior NULs (multiselect separators).</summary>
    private static int GetUsedUtf16ByteLength(byte[] raw)
    {
        int lastNonZero = Array.FindLastIndex(raw, b => b != 0);
        if (lastNonZero < 0)
        {
            return 0;
        }

        // Include final wchar (pair of bytes)
        int len = lastNonZero + 1;
        if ((len & 1) != 0)
        {
            len++;
        }

        return Math.Min(len, raw.Length);
    }

    private static IReadOnlyList<string> ParseMultiSelectResult(string buffer)
    {
        string trimmed = buffer.TrimEnd('\0');
        if (trimmed.Length == 0)
        {
            return Array.Empty<string>();
        }

        string[] parts = trimmed.Split('\0', StringSplitOptions.None);
        if (parts.Length == 1)
        {
            string only = parts[0];
            return string.IsNullOrWhiteSpace(only) ? Array.Empty<string>() : new[] { only };
        }

        string dir = parts[0];
        if (string.IsNullOrWhiteSpace(dir))
        {
            return Array.Empty<string>();
        }

        var list = new List<string>(parts.Length - 1);
        for (int i = 1; i < parts.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(parts[i]))
            {
                continue;
            }

            try
            {
                list.Add(Path.GetFullPath(Path.Combine(dir, parts[i])));
            }
            catch
            {
                // skip invalid paths
            }
        }

        return list;
    }
}
