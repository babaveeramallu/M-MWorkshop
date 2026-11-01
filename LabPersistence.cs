using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Windows.Forms;
using System.Xml;
using Microsoft.Win32;

namespace Cybrslc.Workshop
{
    internal static class LabPersistence
    {
        internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string ValueName = "CYBRSLC&CDRU Lab Marker";
        private const string Description = "CYBRSLC&CDRU consent-based lab marker. No popup demo, workers, or network.";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        internal static string Executable { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        internal static string UserId
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    return identity.User.Value;
            }
        }
        internal static string TaskName { get { return "CYBRSLC&CDRU-Lab-Marker-" + UserId; } }
        internal static string RegistryCommand { get { return "\"" + Executable + "\" --lab-marker registry"; } }
        internal static string MarkerDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CYBRSLC&CDRU", "PersistenceLab"); }
        }

        internal static void WriteMarker(string source)
        {
            if (source != "registry" && source != "task")
                throw new InvalidOperationException("Unknown lab marker source.");
            Directory.CreateDirectory(MarkerDirectory);
            File.WriteAllText(Path.Combine(MarkerDirectory, source + "-last-run.txt"),
                "CYBRSLC&CDRU harmless persistence marker\r\nSource: " + source +
                "\r\nUTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\r\n");
        }

        internal static string BuildTaskXml()
        {
            XmlDocument document = new XmlDocument();
            XmlElement task = document.CreateElement("Task", TaskNamespace);
            task.SetAttribute("version", "1.2");
            document.AppendChild(task);
            XmlElement registration = Add(task, "RegistrationInfo", null);
            Add(registration, "Description", Description);
            XmlElement trigger = Add(Add(task, "Triggers", null), "TimeTrigger", null);
            XmlElement repetition = Add(trigger, "Repetition", null);
            Add(repetition, "Interval", "PT5M");
            Add(repetition, "StopAtDurationEnd", "false");
            Add(trigger, "StartBoundary", DateTime.Now.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            Add(trigger, "Enabled", "true");
            XmlElement principal = Add(Add(task, "Principals", null), "Principal", null);
            principal.SetAttribute("id", "LabUser");
            Add(principal, "UserId", UserId);
            Add(principal, "LogonType", "InteractiveToken");
            Add(principal, "RunLevel", "LeastPrivilege");
            XmlElement settings = Add(task, "Settings", null);
            Add(settings, "MultipleInstancesPolicy", "IgnoreNew");
            Add(settings, "DisallowStartIfOnBatteries", "false");
            Add(settings, "StopIfGoingOnBatteries", "false");
            Add(settings, "ExecutionTimeLimit", "PT1M");
            XmlElement actions = Add(task, "Actions", null);
            actions.SetAttribute("Context", "LabUser");
            XmlElement action = Add(actions, "Exec", null);
            Add(action, "Command", Executable);
            Add(action, "Arguments", "--lab-marker task");
            return document.OuterXml;
        }

        private static XmlElement Add(XmlElement parent, string name, string text)
        {
            XmlElement element = parent.OwnerDocument.CreateElement(name, TaskNamespace);
            if (text != null) element.InnerText = text;
            parent.AppendChild(element);
            return element;
        }

        private static object ReadRunValue()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key == null ? null : key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        private static bool IsOwnedTask(string xml)
        {
            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            document.LoadXml(xml);
            XmlNamespaceManager namespaces = new XmlNamespaceManager(document.NameTable);
            namespaces.AddNamespace("t", TaskNamespace);
            XmlNodeList actions = document.SelectNodes("/t:Task/t:Actions/*", namespaces);
            XmlNode description = document.SelectSingleNode("/t:Task/t:RegistrationInfo/t:Description", namespaces);
            XmlNode command = document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Command", namespaces);
            XmlNode arguments = document.SelectSingleNode("/t:Task/t:Actions/t:Exec/t:Arguments", namespaces);
            return actions.Count == 1 && description != null && description.InnerText == Description &&
                command != null && String.Equals(command.InnerText, Executable, StringComparison.OrdinalIgnoreCase) &&
                arguments != null && arguments.InnerText == "--lab-marker task";
        }

        internal static string Status()
        {
            object value = ReadRunValue();
            using (Scheduler scheduler = new Scheduler())
            {
                string xml = scheduler.TaskXml();
                return "Registry: " + (value == null ? "absent" : Object.Equals(value, RegistryCommand) ? "installed" : "CONFLICT - untouched") +
                    "\r\nScheduled task: " + (xml == null ? "absent" : IsOwnedTask(xml) ? "installed" : "CONFLICT - untouched") +
                    "\r\nTask name: " + TaskName + "\r\nMarker folder: " + MarkerDirectory;
            }
        }

        internal static void Install()
        {
            using (Scheduler scheduler = new Scheduler())
            {
                if (ReadRunValue() != null || scheduler.TaskXml() != null)
                    throw new InvalidOperationException("An entry already exists. Inspect it and remove this lab's entries before installing. Nothing was overwritten.");
                string xml = BuildTaskXml();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) throw new IOException("Cannot open the current-user Run key.");
                    key.SetValue(ValueName, RegistryCommand, RegistryValueKind.String);
                    bool registered = false;
                    try
                    {
                        scheduler.Register(xml);
                        registered = true;
                    }
                    finally
                    {
                        // Roll back only our exact value if task registration is denied or fails.
                        if (!registered && Object.Equals(ReadRunValue(), RegistryCommand))
                            key.DeleteValue(ValueName, false);
                    }
                }
            }
        }

        internal static void Remove()
        {
            using (Scheduler scheduler = new Scheduler())
            {
                object value = ReadRunValue();
                string xml = scheduler.TaskXml();
                if ((value != null && !Object.Equals(value, RegistryCommand)) || (xml != null && !IsOwnedTask(xml)))
                    throw new InvalidOperationException("An entry does not match this executable's lab marker. Nothing removed; inspect it manually.");
                if (xml != null) scheduler.Delete();
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null && Object.Equals(key.GetValue(ValueName), RegistryCommand))
                        key.DeleteValue(ValueName, false);
                }
            }
        }

        internal static int Execute(Action action, bool interactive)
        {
            try { action(); return 0; }
            catch (COMException exception) { return Error(exception, interactive); }
            catch (IOException exception) { return Error(exception, interactive); }
            catch (UnauthorizedAccessException exception) { return Error(exception, interactive); }
            catch (SecurityException exception) { return Error(exception, interactive); }
            catch (InvalidOperationException exception) { return Error(exception, interactive); }
            catch (XmlException exception) { return Error(exception, interactive); }
        }

        private static int Error(Exception exception, bool interactive)
        {
            string message = "CYBRSLC&CDRU persistence lab failed: " + exception.Message +
                "\r\nInspect the lab status before retrying. Do not disable Windows protections.";
            Console.Error.WriteLine(message);
            if (interactive) MessageBox.Show(message, "Lab error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        internal static void ShowControls(IWin32Window owner)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "CYBRSLC&CDRU - real persistence lab (VM only)";
                dialog.ClientSize = new System.Drawing.Size(660, 360);
                dialog.StartPosition = FormStartPosition.CenterParent;
                Label explanation = new Label();
                explanation.SetBounds(16, 16, 628, 115);
                explanation.Text = "Separate opt-in: REAL current-user Run entry + scheduled task.\r\n" +
                    "At logon / every 5 minutes while signed in, write a timestamp only.\r\n" +
                    "No popup demo, workers, network, elevation, hiding, or self-repair.\r\n" +
                    "Entries survive Stop & Exit. Use Remove before deleting/moving the EXE.\r\n" +
                    "Marker text files remain for inspection; no other entries are changed.";
                TextBox state = new TextBox();
                state.SetBounds(16, 136, 628, 150);
                state.Multiline = true;
                state.ReadOnly = true;
                state.ScrollBars = ScrollBars.Both;
                Action refresh = delegate
                {
                    state.Text = "Reading lab state...";
                    if (Execute(delegate { state.Text = Status(); }, true) != 0)
                        state.Text = "ERROR reading lab state. Inspect Registry Editor and Task Scheduler.";
                };
                Button install = new Button();
                install.Text = "Install (VM only)";
                install.SetBounds(16, 302, 190, 40);
                install.Click += delegate
                {
                    if (MessageBox.Show(dialog,
                        "Confirm this is your disposable lab VM.\r\n\r\nInstall a REAL HKCU logon entry and a scheduled task?\r\n" +
                        "They run only the timestamp marker. Entries remain after closing this demo.\r\n" +
                        "No administrator rights or stored password are needed.",
                        "Explicit persistence consent", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    Execute(Install, true);
                    refresh();
                };
                Button remove = new Button();
                remove.Text = "Remove lab entries";
                remove.SetBounds(222, 302, 190, 40);
                remove.Click += delegate { Execute(Remove, true); refresh(); };
                Button inspect = new Button();
                inspect.Text = "Refresh status";
                inspect.SetBounds(428, 302, 190, 40);
                inspect.Click += delegate { refresh(); };
                dialog.Controls.AddRange(new Control[] { explanation, state, install, remove, inspect });
                refresh();
                dialog.ShowDialog(owner);
            }
        }

        private sealed class Scheduler : IDisposable
        {
            private dynamic service;
            private dynamic root;
            internal Scheduler()
            {
                Type type = Type.GetTypeFromProgID("Schedule.Service");
                if (type == null) throw new InvalidOperationException("Windows Task Scheduler is unavailable.");
                service = Activator.CreateInstance(type);
                try
                {
                    service.Connect();
                    root = service.GetFolder("\\");
                }
                finally
                {
                    if (root == null) Dispose();
                }
            }

            internal string TaskXml()
            {
                dynamic task;
                try { task = root.GetTask(TaskName); }
                catch (FileNotFoundException) { return null; }
                catch (COMException exception)
                {
                    if (exception.ErrorCode == unchecked((int)0x80070002)) return null;
                    throw;
                }
                try { return (string)task.Xml; }
                finally { Marshal.ReleaseComObject(task); }
            }

            internal void Register(string xml)
            {
                // TASK_CREATE + INTERACTIVE_TOKEN: never overwrite, elevate, or store credentials.
                object task = root.RegisterTask(TaskName, xml, 2, UserId, null, 3, null);
                Marshal.ReleaseComObject(task);
            }

            internal void Delete() { root.DeleteTask(TaskName, 0); }

            public void Dispose()
            {
                if (root != null) { Marshal.ReleaseComObject(root); root = null; }
                if (service != null) { Marshal.ReleaseComObject(service); service = null; }
            }
        }
    }
}
