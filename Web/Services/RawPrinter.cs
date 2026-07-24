using System.Runtime.InteropServices;
using System.Text;

namespace SimpleERP.Web.Services;

/// <summary>
/// Sends raw ESC/P bytes directly to any Windows printer using Win32 winspool API.
/// Works with USB, LPT, and Serial dot-matrix printers — whatever Windows sees as a printer.
/// No GDI rendering. Bytes go straight through.
/// </summary>
public static class RawPrinter
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private class DOCINFOA {
        [MarshalAs(UnmanagedType.LPStr)] public string pDocName = "SimpleERP";
        [MarshalAs(UnmanagedType.LPStr)] public string? pOutputFile = null;
        [MarshalAs(UnmanagedType.LPStr)] public string pDataType = "RAW";
    }
    [DllImport("winspool.drv", EntryPoint="OpenPrinterA",    SetLastError=true, CharSet=CharSet.Ansi)] private static extern bool OpenPrinter(string n, out IntPtr h, IntPtr d);
    [DllImport("winspool.drv", EntryPoint="ClosePrinter",    SetLastError=true)] private static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", EntryPoint="StartDocPrinterA",SetLastError=true, CharSet=CharSet.Ansi)] private static extern int  StartDocPrinter(IntPtr h, int level, [In, MarshalAs(UnmanagedType.LPStruct)] DOCINFOA di);
    [DllImport("winspool.drv", EntryPoint="EndDocPrinter",   SetLastError=true)] private static extern bool EndDocPrinter(IntPtr h);
    [DllImport("winspool.drv", EntryPoint="StartPagePrinter",SetLastError=true)] private static extern bool StartPagePrinter(IntPtr h);
    [DllImport("winspool.drv", EntryPoint="EndPagePrinter",  SetLastError=true)] private static extern bool EndPagePrinter(IntPtr h);
    [DllImport("winspool.drv", EntryPoint="WritePrinter",    SetLastError=true)] private static extern bool WritePrinter(IntPtr h, IntPtr pBytes, int dwCount, out int dwWritten);

    public static PrintJobResult Send(string printerName, byte[] bytes)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return PrintJobResult.Fail("Raw printing only works on Windows.");
        if (string.IsNullOrWhiteSpace(printerName))
            return PrintJobResult.Fail("No printer configured. Go to Settings → Printer.");

        if (!OpenPrinter(printerName.Trim(), out var hPrinter, IntPtr.Zero))
            return PrintJobResult.Fail($"Cannot open printer '{printerName}'. Check the name in Settings → Printer.");

        try {
            if (StartDocPrinter(hPrinter, 1, new DOCINFOA()) == 0)
                return PrintJobResult.Fail("Failed to start print job.");
            StartPagePrinter(hPrinter);
            var ptr = Marshal.AllocCoTaskMem(bytes.Length);
            try { Marshal.Copy(bytes, 0, ptr, bytes.Length); WritePrinter(hPrinter, ptr, bytes.Length, out _); }
            finally { Marshal.FreeCoTaskMem(ptr); }
            EndPagePrinter(hPrinter);
            EndDocPrinter(hPrinter);
            return PrintJobResult.Ok();
        } finally { ClosePrinter(hPrinter); }
    }

    public static List<string> GetInstalledPrinters()
    {
        var list = new List<string>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return list;
        try { foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters) list.Add(p); } catch { }
        return list;
    }
}

public class PrintJobResult {
    public bool Success { get; private set; }
    public string? Error { get; private set; }
    public static PrintJobResult Ok() => new() { Success = true };
    public static PrintJobResult Fail(string e) => new() { Success = false, Error = e };
}
