using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static ClaudeGui.Blazor.Services.ConPTY.Native.ProcessApi;
using static ClaudeGui.Blazor.Services.ConPTY.Native.PseudoConsoleApi;

namespace ClaudeGui.Blazor.Services.ConPTY;

/// <summary>
/// Gestisce un processo lanciato tramite Windows ConPTY (Pseudo Console).
/// Fornisce accesso agli stream di input/output e supporta il ridimensionamento del terminal.
/// </summary>
public sealed class Terminal : IDisposable
{
    private PseudoConsole? _pseudoConsole;
    private FileStream? _consoleInputWriter;
    private FileStream? _consoleOutputReader;
    private IntPtr _processHandle;
    private IntPtr _threadHandle;
    private bool _disposed;

    /// <summary>
    /// Process ID del processo lanciato.
    /// </summary>
    public int Pid { get; private set; }

    /// <summary>
    /// Indica se il processo è ancora in esecuzione.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Evento sollevato quando il processo termina.
    /// </summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>
    /// Avvia un processo tramite ConPTY.
    /// </summary>
    /// <param name="command">Comando da eseguire (es. "claude --dangerously-skip-permissions")</param>
    /// <param name="workingDirectory">Working directory per il processo</param>
    /// <param name="rows">Altezza del terminal in righe</param>
    /// <param name="cols">Larghezza del terminal in colonne</param>
    public void Start(string command, string workingDirectory, int rows, int cols)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Process is already running");
        }

        // 1. Crea pipe per input (keyboard -> PTY)
        CreatePipe(out SafeFileHandle inputPipeRead, out SafeFileHandle inputPipeWrite, IntPtr.Zero, 0);

        // 2. Crea pipe per output (PTY -> screen)
        CreatePipe(out SafeFileHandle outputPipeRead, out SafeFileHandle outputPipeWrite, IntPtr.Zero, 0);

        // 3. Crea PseudoConsole
        _pseudoConsole = PseudoConsole.Create(inputPipeRead, outputPipeWrite, cols, rows);

        // 4. Chiudi i lati delle pipe che non ci servono più
        // Il PTY ora possiede inputPipeRead e outputPipeWrite
        inputPipeRead.Dispose();
        outputPipeWrite.Dispose();

        // 5. Crea gli stream per leggere/scrivere
        _consoleInputWriter = new FileStream(inputPipeWrite, FileAccess.Write);
        _consoleOutputReader = new FileStream(outputPipeRead, FileAccess.Read);

        // 6. Prepara STARTUPINFOEX per lanciare il processo con PTY
        var startupInfo = ConfigureProcessThread(_pseudoConsole.Handle);

        // 7. Lancia il processo
        var processInfo = RunProcess(command, workingDirectory, ref startupInfo);

        // 8. Salva handle e PID
        _processHandle = processInfo.hProcess;
        _threadHandle = processInfo.hThread;
        Pid = processInfo.dwProcessId;
        IsRunning = true;

        // 9. Monitora terminazione processo in background
        _ = Task.Run(async () => await MonitorProcessExitAsync());
    }

    /// <summary>
    /// Legge l'output del processo in modo asincrono.
    /// </summary>
    /// <param name="buffer">Buffer in cui scrivere i dati</param>
    /// <returns>Numero di byte letti (0 se il processo è terminato)</returns>
    public async Task<int> ReadOutputAsync(byte[] buffer)
    {
        if (_consoleOutputReader == null)
        {
            throw new InvalidOperationException("Process not started");
        }

        try
        {
            return await _consoleOutputReader.ReadAsync(buffer, 0, buffer.Length);
        }
        catch (IOException)
        {
            // Pipe chiusa, processo terminato
            return 0;
        }
    }

    /// <summary>
    /// Scrive input al processo in modo asincrono.
    /// </summary>
    /// <param name="input">Stringa da inviare al processo</param>
    public async Task WriteInputAsync(string input)
    {
        if (_consoleInputWriter == null)
        {
            throw new InvalidOperationException("Process not started");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        await _consoleInputWriter.WriteAsync(bytes, 0, bytes.Length);
        await _consoleInputWriter.FlushAsync();
    }

    /// <summary>
    /// Ridimensiona il terminal PTY.
    /// </summary>
    /// <param name="cols">Nuova larghezza in colonne</param>
    /// <param name="rows">Nuova altezza in righe</param>
    public void Resize(int cols, int rows)
    {
        if (_pseudoConsole == null)
        {
            throw new InvalidOperationException("Process not started");
        }

        _pseudoConsole.Resize(cols, rows);
    }

    /// <summary>
    /// Termina forzatamente il processo.
    /// </summary>
    public void Kill()
    {
        if (_processHandle != IntPtr.Zero && IsRunning)
        {
            TerminateProcess(_processHandle, 0);
            IsRunning = false;
        }
    }

    /// <summary>
    /// Attende la terminazione del processo.
    /// </summary>
    /// <param name="milliseconds">Timeout in millisecondi</param>
    /// <returns>True se il processo è terminato entro il timeout</returns>
    public bool WaitForExit(int milliseconds)
    {
        if (_processHandle == IntPtr.Zero)
        {
            return true;
        }

        var result = WaitForSingleObject(_processHandle, (uint)milliseconds);
        return result == WAIT_OBJECT_0;
    }

    /// <summary>
    /// Ottiene l'exit code del processo (valido solo dopo la terminazione).
    /// </summary>
    public int GetExitCode()
    {
        if (_processHandle == IntPtr.Zero)
        {
            return -1;
        }

        GetExitCodeProcess(_processHandle, out int exitCode);
        return exitCode;
    }

    /// <summary>
    /// Monitora la terminazione del processo e solleva l'evento ProcessExited.
    /// </summary>
    private async Task MonitorProcessExitAsync()
    {
        // Attendi infinitamente che il processo termini
        await Task.Run(() => WaitForSingleObject(_processHandle, INFINITE));

        IsRunning = false;
        var exitCode = GetExitCode();

        // Solleva evento
        ProcessExited?.Invoke(this, exitCode);
    }

    /// <summary>
    /// Configura STARTUPINFOEX con l'attributo PseudoConsole.
    /// </summary>
    private STARTUPINFOEX ConfigureProcessThread(IntPtr hPC)
    {
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // Alloca attribute list
        IntPtr lpSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref lpSize);

        startupInfo.lpAttributeList = Marshal.AllocHGlobal(lpSize);
        if (!InitializeProcThreadAttributeList(startupInfo.lpAttributeList, 1, 0, ref lpSize))
        {
            throw new InvalidOperationException("Could not initialize proc thread attribute list. Error: " + Marshal.GetLastWin32Error());
        }

        // Imposta attributo PseudoConsole
        if (!UpdateProcThreadAttribute(
            startupInfo.lpAttributeList,
            0,
            PseudoConsole.PseudoConsoleThreadAttribute,
            hPC,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            throw new InvalidOperationException("Could not update proc thread attribute. Error: " + Marshal.GetLastWin32Error());
        }

        return startupInfo;
    }

    /// <summary>
    /// Lancia il processo usando CreateProcess con STARTUPINFOEX.
    /// </summary>
    private PROCESS_INFORMATION RunProcess(string command, string workingDirectory, ref STARTUPINFOEX startupInfo)
    {
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>()
        };

        if (!CreateProcess(
            null,
            command,
            ref securityAttributes,
            ref securityAttributes,
            true, // inheritHandles
            EXTENDED_STARTUPINFO_PRESENT,
            IntPtr.Zero,
            workingDirectory,
            ref startupInfo,
            out PROCESS_INFORMATION processInfo))
        {
            throw new InvalidOperationException("Could not create process. Error: " + Marshal.GetLastWin32Error());
        }

        return processInfo;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Termina processo se ancora in esecuzione
        if (IsRunning)
        {
            Kill();
        }

        // Chiudi stream
        _consoleInputWriter?.Dispose();
        _consoleOutputReader?.Dispose();

        // Chiudi PseudoConsole
        _pseudoConsole?.Dispose();

        // Chiudi handle processo e thread
        if (_processHandle != IntPtr.Zero)
        {
            CloseHandle(_processHandle);
            _processHandle = IntPtr.Zero;
        }

        if (_threadHandle != IntPtr.Zero)
        {
            CloseHandle(_threadHandle);
            _threadHandle = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);
}
