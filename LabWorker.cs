using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;

[assembly: AssemblyDescription("Consent-based Task Manager exercise; not a Windows component")]
[assembly: AssemblyCompany("CYBRSLC&CDRU Workshop")]
[assembly: AssemblyProduct("CYBRSLC&CDRU offline simulation")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace Cybrslc.Workshop
{
    internal static class LabWorker
    {
        private static int Main(string[] args)
        {
            int parentId;
            long parentStart;
            if (args.Length != 4 || args[0] != "--cybrslc-demo" ||
                !Int32.TryParse(args[1], out parentId) ||
                !Int64.TryParse(args[2], out parentStart) ||
                !args[3].StartsWith("Local\\CYBRSLC&CDRU-Stop-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("CYBRSLC&CDRU lab worker: launch only through the consenting demo controller.");
                return 2;
            }

            try
            {
                using (Process parent = Process.GetProcessById(parentId))
                using (EventWaitHandle stop = EventWaitHandle.OpenExisting(args[3]))
                using (SHA256 hash = SHA256.Create())
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != parentStart)
                    {
                        Console.Error.WriteLine("Parent identity mismatch; worker exiting.");
                        return 1;
                    }
                    // Retain the actual parent handle so PID reuse cannot keep a worker alive.
                    IntPtr handle = parent.Handle;
                    byte[] state = new byte[32];
                    Console.WriteLine("READY - CYBRSLC&CDRU worker; waiting and hashing in memory only.");
                    Console.Out.Flush();
                    while (!stop.WaitOne(0))
                    {
                        if (parent.WaitForExit(1000)) break;
                        state = hash.ComputeHash(state);
                    }
                }
                return 0;
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("Controller no longer available: " + exception.Message);
                return 1;
            }
            catch (WaitHandleCannotBeOpenedException exception)
            {
                Console.Error.WriteLine("Controller stop signal unavailable: " + exception.Message);
                return 1;
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                Console.Error.WriteLine("Cannot monitor controller: " + exception.Message);
                return 1;
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine("Controller state unavailable: " + exception.Message);
                return 1;
            }
            catch (UnauthorizedAccessException exception)
            {
                Console.Error.WriteLine("Worker access denied: " + exception.Message);
                return 1;
            }
            catch (System.Security.Cryptography.CryptographicException exception)
            {
                Console.Error.WriteLine("Worker hash initialization failed: " + exception.Message);
                return 1;
            }
        }
    }
}
