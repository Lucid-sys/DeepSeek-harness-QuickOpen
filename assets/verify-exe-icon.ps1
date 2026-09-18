# Verify that a Windows executable really carries an icon resource, and which
# sizes it contains. Reads the PE resource directory through the Win32 API
# (EnumResourceNames / FindResource / LoadResource) instead of trusting a
# file-size delta.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads .ps1 files using the
# system ANSI code page unless they carry a BOM, so non-ASCII comments here
# could be misread. Chinese notes live in README.md instead.
#
# Usage:  powershell -File verify-exe-icon.ps1 <path-to-exe>

param([Parameter(Mandatory = $true)][string]$Exe)

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class PeIcon
{
    const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    static readonly IntPtr RT_ICON = new IntPtr(3);
    static readonly IntPtr RT_GROUP_ICON = new IntPtr(14);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibraryExW(string file, IntPtr reserved, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr module);

    delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool EnumResourceNamesW(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr param);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr FindResourceW(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LockResource(IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint SizeofResource(IntPtr module, IntPtr resource);

    /// <summary>Sizes (width in pixels) of every frame inside every RT_GROUP_ICON.</summary>
    public static List<int> ReadIconSizes(string path, out int iconCount, out int groupCount)
    {
        // The C# 5 compiler behind Add-Type rejects ref/out parameters inside
        // lambdas, so the callbacks accumulate into locals and we copy out at
        // the end.
        var sizes = new List<int>();
        int icons = 0;
        int groups = 0;

        IntPtr module = LoadLibraryExW(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero) throw new Exception("LoadLibraryEx failed: " + Marshal.GetLastWin32Error());

        try
        {
            // Count the raw RT_ICON frames (each directory entry is one image).
            EnumResourceNamesW(module, RT_ICON, (m, t, n, p) => { icons++; return true; }, IntPtr.Zero);

            // Walk each icon group and read its GRPICONDIRENTRY table.
            EnumResourceNamesW(module, RT_GROUP_ICON, (m, t, n, p) =>
            {
                groups++;
                IntPtr handle = FindResourceW(m, n, RT_GROUP_ICON);
                if (handle == IntPtr.Zero) return true;
                uint size = SizeofResource(m, handle);
                IntPtr loaded = LoadResource(m, handle);
                if (loaded == IntPtr.Zero || size < 6) return true;
                IntPtr data = LockResource(loaded);
                if (data == IntPtr.Zero) return true;

                int count = Marshal.ReadInt16(data, 4);
                for (int i = 0; i < count; i++)
                {
                    int entry = 6 + i * 14;
                    if (entry + 14 > size) break;
                    int width = Marshal.ReadByte(data, entry);
                    sizes.Add(width == 0 ? 256 : width);
                }
                return true;
            }, IntPtr.Zero);
        }
        finally
        {
            FreeLibrary(module);
        }

        sizes.Sort();
        iconCount = icons;
        groupCount = groups;
        return sizes;
    }
}
'@

if (-not (Test-Path -LiteralPath $Exe)) { Write-Error "not found: $Exe"; exit 2 }

$iconCount = 0
$groupCount = 0
$sizes = [PeIcon]::ReadIconSizes($Exe, [ref]$iconCount, [ref]$groupCount)

Write-Output "exe           : $Exe"
Write-Output "size          : $((Get-Item -LiteralPath $Exe).Length) bytes"
Write-Output "RT_ICON frames: $iconCount"
Write-Output "icon groups   : $groupCount"
Write-Output "frame widths  : $($sizes -join ', ')"

if ($iconCount -eq 0) { Write-Output "RESULT: NO ICON RESOURCE EMBEDDED"; exit 1 }
if ($sizes -notcontains 256) { Write-Output "RESULT: icon present but missing the 256px frame"; exit 1 }
Write-Output "RESULT: icon resource embedded with all expected sizes"
exit 0
