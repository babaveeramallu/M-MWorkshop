using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace Cybrslc.Workshop
{
    internal static class Program
    {
        private const string Marker = "SIMULATED_MALWARE_TELEMETRY_PING";
        private const string WorkshopId = "CYBRSLC&CDRU_BENIGN_SIMULATOR_V1";
        private const string TelemetryUrl = "http://192.168.56.20/ping?id=" + Marker;
        private static volatile bool cancelled;

        private static int Main(string[] args)
        {
            bool consent = false;
            bool fast = false;
            bool telemetry = false;
            foreach (string argument in args)
            {
                switch (argument)
                {
                    case "--consent": consent = true; break;
                    case "--fast": fast = true; break;
                    case "--telemetry": telemetry = true; break;
                    case "--help":
                        Console.WriteLine("Usage: SimulatedPayload.exe [--consent] [--fast] [--telemetry]");
                        Console.WriteLine("--consent: explicitly opt in without an interactive prompt.");
                        Console.WriteLine("--fast: skip animation delays.");
                        Console.WriteLine("--telemetry: one GET to the fixed lab IP 192.168.56.20:80.");
                        Console.WriteLine("Default: no network, persistence, or application file writes.");
                        return 0;
                    default:
                        Console.Error.WriteLine("Unknown argument: " + argument + ". Use --help.");
                        return 2;
                }
            }

            Console.WriteLine("CYBRSLC&CDRU | BENIGN WORKSHOP SIMULATION | NOT A REAL COMPROMISE");
            Console.WriteLine("All hacker actions below are fictional. No files are encrypted or deleted.");
            Console.WriteLine("No startup entries, child processes, credential access, or input blocking.");
            Console.WriteLine("Network: " + (telemetry ? "ONE LAB-ONLY REQUEST (explicitly enabled)." : "OFF."));
            Console.WriteLine("Press Esc or Ctrl+C, or close this window, to stop.");
            if (!consent)
            {
                if (Console.IsInputRedirected)
                {
                    Console.Error.WriteLine("Consent required. Run interactively or explicitly supply --consent.");
                    return 2;
                }
                Console.Write("Type TROLL to opt in, or anything else to exit: ");
                consent = String.Equals(Console.ReadLine(), "TROLL", StringComparison.Ordinal);
                if (!consent)
                {
                    Console.WriteLine("Not started. No simulation actions performed.");
                    return 2;
                }
            }

            ConsoleCancelEventHandler cancelHandler = delegate(object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cancelled = true;
            };
            Console.CancelKeyPress += cancelHandler;
            ConsoleColor originalColor = Console.ForegroundColor;
            try
            {
                if (!Console.IsOutputRedirected) Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(WorkshopId);
                Console.WriteLine(Marker);
                string[] scenes =
                {
                    "[SIMULATION] Connecting to the totally real hacker mainframe...",
                    "[SIMULATION] Bypassing the imaginary firewall with a student discount...",
                    "[SIMULATION] Downloading extra RAM... budget request denied.",
                    "[SIMULATION] Cracking password 'password'... asking it nicely.",
                    "[SIMULATION] Locating homework... 404: motivation not found.",
                    "[SIMULATION] Encrypting your vibes... vibes already scrambled.",
                    "[SIMULATION] Installing persistence... JUST KIDDING. No registry writes.",
                    "[SIMULATION] Exfiltrating snacks... vending machine declined the connection.",
                    "[SIMULATION] Escalating privileges... promoted to group-project leader.",
                    "[SIMULATION] Deploying ransomware... ransom is one rubber duck.",
                    "[SIMULATION] Deleting System32... NOPE. We like our computers.",
                    "[SIMULATION] Threat level: academically unserious."
                };

                for (int i = 0; i < scenes.Length; i++)
                {
                    if (ShouldStop()) return Stopped();
                    Console.WriteLine("[{0,3}%] {1}", (i + 1) * 100 / scenes.Length, scenes[i]);
                    if (!Pause(fast ? 0 : 700)) return Stopped();
                }

                if (ShouldStop()) return Stopped();
                bool requestSucceeded = !telemetry || SendLabTelemetry();
                if (ShouldStop()) return Stopped();
                Console.WriteLine();
                Console.WriteLine("REVEAL: You were not hacked. Your snacks remain unexfiltrated.");
                Console.WriteLine("The terminal drama was fake. Only explicitly enabled lab HTTP is real.");
                Console.WriteLine("No application files or startup entries were created. Closing normally.");
                return requestSucceeded ? 0 : 1;
            }
            finally
            {
                if (!Console.IsOutputRedirected) Console.ForegroundColor = originalColor;
                Console.CancelKeyPress -= cancelHandler;
            }
        }

        private static bool ShouldStop()
        {
            if (!Console.IsInputRedirected && Console.KeyAvailable)
            {
                if (Console.ReadKey(true).Key == ConsoleKey.Escape) cancelled = true;
            }
            return cancelled;
        }

        private static bool Pause(int milliseconds)
        {
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < milliseconds)
            {
                if (ShouldStop()) return false;
                Thread.Sleep(25);
            }
            return !cancelled;
        }

        private static int Stopped()
        {
            Console.WriteLine("Simulation stopped by participant.");
            return 130;
        }

        private static bool SendLabTelemetry()
        {
            Console.WriteLine("[REAL LAB HTTP] GET " + TelemetryUrl);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(TelemetryUrl);
            request.Proxy = null;
            request.AllowAutoRedirect = false;
            request.Timeout = 2000;
            request.ReadWriteTimeout = 2000;
            request.KeepAlive = false;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    Console.WriteLine("[REAL LAB HTTP] Status: " + status + ". Response body not read or executed.");
                    if (status < 200 || status >= 300)
                    {
                        Console.Error.WriteLine("Telemetry failed: expected a 2xx response. Redirects are disabled.");
                        return false;
                    }
                    return true;
                }
            }
            catch (WebException exception)
            {
                if (exception.Response != null) exception.Response.Dispose();
                Console.Error.WriteLine("Telemetry failed: " + exception.Status + ": " + exception.Message);
                return false;
            }
        }
    }
}
