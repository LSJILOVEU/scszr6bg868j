using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml;

namespace WarCore.Verification.Unity
{
    // macOS only. IL2CPP does not implement System.Diagnostics.Process.Start.
    internal static class MacProbeProcess
    {
        private const string LibSystem = "/usr/lib/libSystem.B.dylib";
        [DllImport(LibSystem, EntryPoint = "posix_spawn")]
        private static extern int Spawn(out int pid, IntPtr path, IntPtr actions, IntPtr attributes, IntPtr argv, IntPtr env);
        [DllImport(LibSystem, EntryPoint = "_NSGetEnviron")]
        private static extern IntPtr GetEnviron();
        [DllImport(LibSystem, EntryPoint = "waitpid", SetLastError = true)]
        private static extern int WaitPid(int pid, out int status, int options);
        [DllImport(LibSystem, EntryPoint = "kill", SetLastError = true)]
        private static extern int Kill(int pid, int signal);

        internal static string Executable(string dataPath)
        {
            // Unity macOS dataPath is <app>/Contents. Do not infer the binary from the .app name.
            string contents = Path.GetFullPath(dataPath);
            string executable = null;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var reader = XmlReader.Create(Path.Combine(contents, "Info.plist"), settings))
            {
                var plist = new XmlDocument { XmlResolver = null };
                plist.Load(reader);
                XmlNode key = plist.SelectSingleNode("/plist/dict/key[.='CFBundleExecutable']");
                XmlNode value = key?.NextSibling;
                if (value?.Name == "string") executable = value.InnerText;
            }
            if (string.IsNullOrEmpty(executable) || executable == "." || executable == ".."
                || executable.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
                throw new InvalidDataException("Missing or invalid CFBundleExecutable in " + contents);
            string path = Path.Combine(contents, "MacOS", executable);
            if (!File.Exists(path)) throw new FileNotFoundException("Raw probe executable is missing.", path);
            return path;
        }

        // Return the unmodified Darwin wait status, including the signal/core-dump bits.
        internal static int Run(string[] arguments, int timeoutMilliseconds)
        {
            if (arguments == null || arguments.Length == 0 || !Path.IsPathRooted(arguments[0]))
                throw new ArgumentException("Raw probe requires an absolute executable path.");
            if (timeoutMilliseconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            using (var argv = new NativeArguments(arguments))
            {
                int error = Spawn(out int pid, Marshal.ReadIntPtr(argv.Pointer), IntPtr.Zero, IntPtr.Zero,
                    argv.Pointer, Marshal.ReadIntPtr(GetEnviron()));
                if (error != 0) throw new IOException("posix_spawn failed: errno=" + error);
                var timer = Stopwatch.StartNew();
                while (true)
                {
                    int result = WaitPid(pid, out int status, 1); // Darwin WNOHANG
                    if (result == pid) return status;
                    if (result < 0 && Marshal.GetLastWin32Error() != 4) // EINTR
                        throw new IOException("waitpid failed: errno=" + Marshal.GetLastWin32Error());
                    if (timer.ElapsedMilliseconds >= timeoutMilliseconds)
                    {
                        // Kill only the owned child and reap it. arch execs the requested binary.
                        if (Kill(pid, 9) != 0 && Marshal.GetLastWin32Error() != 3) // SIGKILL, ESRCH
                            throw new IOException("kill failed: errno=" + Marshal.GetLastWin32Error());
                        do { result = WaitPid(pid, out status, 0); }
                        while (result < 0 && Marshal.GetLastWin32Error() == 4);
                        if (result != pid) throw new IOException("Could not reap timed-out raw probe.");
                        throw new TimeoutException("Raw probe child timed out; wait-status=" + status);
                    }
                    Thread.Sleep(10);
                }
            }
        }

        internal static int Signal(int status) => status & 0x7f;
        internal static int ExitCode(int status) => Signal(status) == 0 ? (status >> 8) & 0xff : 128 + Signal(status);

        private sealed class NativeArguments : IDisposable
        {
            internal IntPtr Pointer { get; private set; }
            private readonly IntPtr[] strings;
            internal NativeArguments(string[] arguments)
            {
                strings = new IntPtr[arguments.Length];
                try
                {
                    Pointer = Marshal.AllocHGlobal((arguments.Length + 1) * IntPtr.Size);
                    for (int i = 0; i < arguments.Length; i++)
                    {
                        if (arguments[i] == null || arguments[i].IndexOf('\0') >= 0)
                            throw new ArgumentException("Invalid raw probe argument.");
                        byte[] utf8 = Encoding.UTF8.GetBytes(arguments[i] + "\0");
                        strings[i] = Marshal.AllocHGlobal(utf8.Length);
                        Marshal.Copy(utf8, 0, strings[i], utf8.Length);
                        Marshal.WriteIntPtr(Pointer, i * IntPtr.Size, strings[i]);
                    }
                    Marshal.WriteIntPtr(Pointer, arguments.Length * IntPtr.Size, IntPtr.Zero);
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                foreach (IntPtr value in strings) if (value != IntPtr.Zero) Marshal.FreeHGlobal(value);
                if (Pointer != IntPtr.Zero) Marshal.FreeHGlobal(Pointer);
                Pointer = IntPtr.Zero;
            }
        }
    }
}
