using Microsoft.Win32.SafeHandles;
using System;
using static ClaudeGui.Blazor.Services.ConPTY.Native.PseudoConsoleApi;

namespace ClaudeGui.Blazor.Services.ConPTY;

/// <summary>
/// Utility functions around the new Pseudo Console APIs
/// </summary>
internal sealed class PseudoConsole : IDisposable
{
    public static readonly IntPtr PseudoConsoleThreadAttribute = (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE;

    public IntPtr Handle { get; }

    private PseudoConsole(IntPtr handle)
    {
        this.Handle = handle;
    }

    /// <summary>
    /// Crea un nuovo PseudoConsole con le dimensioni specificate.
    /// </summary>
    /// <param name="inputReadSide">Handle per la lettura dell'input (pipe)</param>
    /// <param name="outputWriteSide">Handle per la scrittura dell'output (pipe)</param>
    /// <param name="width">Larghezza del terminal in colonne</param>
    /// <param name="height">Altezza del terminal in righe</param>
    /// <returns>Istanza di PseudoConsole</returns>
    internal static PseudoConsole Create(SafeFileHandle inputReadSide, SafeFileHandle outputWriteSide, int width, int height)
    {
        var createResult = CreatePseudoConsole(
            new COORD { X = (short)width, Y = (short)height },
            inputReadSide, outputWriteSide,
            0, out IntPtr hPC);
        if (createResult != 0)
        {
            throw new InvalidOperationException("Could not create pseudo console. Error Code " + createResult);
        }
        return new PseudoConsole(hPC);
    }

    /// <summary>
    /// Ridimensiona il PseudoConsole.
    /// </summary>
    /// <param name="width">Nuova larghezza in colonne</param>
    /// <param name="height">Nuova altezza in righe</param>
    public void Resize(int width, int height)
    {
        var resizeResult = ResizePseudoConsole(Handle, new COORD { X = (short)width, Y = (short)height });
        if (resizeResult != 0)
        {
            throw new InvalidOperationException("Could not resize pseudo console. Error Code " + resizeResult);
        }
    }

    public void Dispose()
    {
        ClosePseudoConsole(Handle);
    }
}
