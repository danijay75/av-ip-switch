using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace EasyIpSwitch
{
    internal static class AppShortcutTests
    {
        private static int _assertions;

        public static int Run()
        {
            CopiesToDocumentsAndCreatesDesktopShortcut();
            RunningFromDocumentsDoesNotFailAndReusesShortcut();
            UpdatesExistingAppShortcutWithoutDuplicates();
            PreservesUnrelatedUserFileOnDesktop();
            IsInstalledRequiresMatchingDocumentsCopyAndDesktopShortcut();
            MainFormPlacesShortcutButtonAndInvokesBackend();
            MainFormHidesShortcutButtonWhenInstalledAndShowsOnFailure();
            return _assertions;
        }

        private static string CreateTempDir(string prefix)
        {
            string path = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDir(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private static void CopiesToDocumentsAndCreatesDesktopShortcut()
        {
            string sourceDir = CreateTempDir("AvIpSwitch-src");
            string docsDir = CreateTempDir("AvIpSwitch-docs");
            string desktopDir = CreateTempDir("AvIpSwitch-desk");
            try
            {
                string sourceExe = Path.Combine(sourceDir, "AV IP-Switch-setup.exe");
                byte[] payload = new byte[] { 1, 2, 3, 4, 5, 6 };
                File.WriteAllBytes(sourceExe, payload);

                AppShortcutResult result = AppShortcut.Install(sourceExe, docsDir, desktopDir, null);
                string expectedCopy = Path.Combine(docsDir, "AV IP-Switch v1.4.exe");
                string expectedLink = Path.Combine(desktopDir, "AV IP-Switch.lnk");

                Assert(result.Success && result.Copied && result.ShortcutCreated,
                    "Initial install must copy to Documents and create the Desktop shortcut.");
                Assert(string.Equals(result.CopyPath, expectedCopy, StringComparison.OrdinalIgnoreCase) && File.Exists(expectedCopy),
                    "Copy must live directly in Documents with the versioned filename.");
                Assert(string.Equals(result.ShortcutPath, expectedLink, StringComparison.OrdinalIgnoreCase) && File.Exists(expectedLink),
                    "Desktop shortcut must be created.");

                string target;
                Assert(AppShortcut.TryReadShortcutTarget(expectedLink, out target)
                    && string.Equals(Path.GetFullPath(target), Path.GetFullPath(expectedCopy), StringComparison.OrdinalIgnoreCase),
                    "Desktop shortcut must target the Documents executable.");
            }
            finally
            {
                DeleteDir(sourceDir);
                DeleteDir(docsDir);
                DeleteDir(desktopDir);
            }
        }

        private static void RunningFromDocumentsDoesNotFailAndReusesShortcut()
        {
            string docsDir = CreateTempDir("AvIpSwitch-docs");
            string desktopDir = CreateTempDir("AvIpSwitch-desk");
            try
            {
                string docsExe = Path.Combine(docsDir, "AV IP-Switch v1.4.exe");
                File.WriteAllBytes(docsExe, new byte[] { 7, 8, 9 });

                AppShortcutResult first = AppShortcut.Install(docsExe, docsDir, desktopDir, null);
                Assert(first.Success && first.AlreadyAtDestination && first.ShortcutCreated,
                    "Running directly from Documents must skip copying and create the shortcut.");

                AppShortcutResult second = AppShortcut.Install(docsExe, docsDir, desktopDir, null);
                Assert(second.Success && second.AlreadyAtDestination && !second.ShortcutCreated && !second.ShortcutUpdated,
                    "Re-running from Documents must reuse the existing shortcut without error.");
                Assert(Directory.GetFiles(desktopDir).Length == 1,
                    "Re-running must not create duplicate shortcuts.");
            }
            finally
            {
                DeleteDir(docsDir);
                DeleteDir(desktopDir);
            }
        }

        private static void UpdatesExistingAppShortcutWithoutDuplicates()
        {
            string sourceDir = CreateTempDir("AvIpSwitch-src");
            string docsDir = CreateTempDir("AvIpSwitch-docs");
            string desktopDir = CreateTempDir("AvIpSwitch-desk");
            try
            {
                string oldDocsExe = Path.Combine(docsDir, "AV IP-Switch v1.2.1.exe");
                File.WriteAllBytes(oldDocsExe, new byte[] { 10, 11 });
                string linkPath = Path.Combine(desktopDir, "AV IP-Switch.lnk");

                object shell = new ShellLinkCom();
                try
                {
                    IShellLinkW link = (IShellLinkW)shell;
                    link.SetPath(oldDocsExe);
                    link.SetWorkingDirectory(docsDir);
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)shell).Save(linkPath, true);
                }
                finally
                {
                    if (System.Runtime.InteropServices.Marshal.IsComObject(shell))
                    {
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    }
                }

                string newSourceExe = Path.Combine(sourceDir, "AV IP-Switch.exe");
                File.WriteAllBytes(newSourceExe, new byte[] { 20, 21, 22 });

                AppShortcutResult result = AppShortcut.Install(newSourceExe, docsDir, desktopDir, null);
                string expectedCopy = Path.Combine(docsDir, "AV IP-Switch v1.4.exe");
                string target;

                Assert(result.Success && result.Copied && result.ShortcutUpdated && !result.ShortcutCreated,
                    "An existing AV IP-Switch shortcut must be updated to v1.4.");
                Assert(Directory.GetFiles(desktopDir).Length == 1,
                    "Updating an existing app shortcut must not leave duplicates.");
                Assert(AppShortcut.TryReadShortcutTarget(linkPath, out target)
                    && string.Equals(Path.GetFullPath(target), Path.GetFullPath(expectedCopy), StringComparison.OrdinalIgnoreCase),
                    "Updated shortcut must target the v1.4 copy in Documents.");
            }
            finally
            {
                DeleteDir(sourceDir);
                DeleteDir(docsDir);
                DeleteDir(desktopDir);
            }
        }

        private static void PreservesUnrelatedUserFileOnDesktop()
        {
            string sourceDir = CreateTempDir("AvIpSwitch-src");
            string docsDir = CreateTempDir("AvIpSwitch-docs");
            string desktopDir = CreateTempDir("AvIpSwitch-desk");
            try
            {
                string sourceExe = Path.Combine(sourceDir, "AV IP-Switch.exe");
                File.WriteAllBytes(sourceExe, new byte[] { 30, 31 });

                string userFile = Path.Combine(desktopDir, "AV IP-Switch.lnk");
                const string sentinel = "User file; must remain intact.";
                File.WriteAllText(userFile, sentinel);

                AppShortcutResult result = AppShortcut.Install(sourceExe, docsDir, desktopDir, null);
                string expectedAlt = Path.Combine(desktopDir, "AV IP-Switch (2).lnk");

                Assert(result.Success && result.ShortcutCreated,
                    "A separate shortcut must be created when an unrelated file exists.");
                Assert(string.Equals(result.ShortcutPath, expectedAlt, StringComparison.OrdinalIgnoreCase),
                    "Alternate shortcut must be named AV IP-Switch (2).lnk.");
                Assert(File.ReadAllText(userFile) == sentinel,
                    "Existing unrelated user file must never be overwritten.");
            }
            finally
            {
                DeleteDir(sourceDir);
                DeleteDir(docsDir);
                DeleteDir(desktopDir);
            }
        }

        private static void MainFormPlacesShortcutButtonAndInvokesBackend()
        {
            using (MainForm form = new MainForm())
            {
                form.Size = form.MinimumSize;
                AppLanguage[] langs = new AppLanguage[]
                {
                    AppLanguage.French, AppLanguage.English, AppLanguage.German, AppLanguage.Spanish
                };
                for (int i = 0; i < langs.Length; i++)
                {
                    L.SetLanguage(langs[i], false);
                    form.ApplyLanguage();
                    TabBar tabs = Field<TabBar>(form, "_tabs");
                    FlatButton btnShortcut = Field<FlatButton>(form, "_btnShortcut");
                    FlatButton btnConnections = Field<FlatButton>(form, "_btnConnections");
                    Panel content = Field<Panel>(form, "_content");

                    Assert(btnShortcut.Right < btnConnections.Left,
                        "Shortcut button must be placed to the left of Network connections.");
                    Assert(tabs.Right <= btnShortcut.Left,
                        "Tabs must not overlap the shortcut button in " + langs[i] + ".");
                    Assert(btnConnections.Right <= content.Width - Theme.S(24),
                        "Action buttons must stay within the content margin in " + langs[i] + ".");
                }
                L.SetLanguage(AppLanguage.French, false);
                form.ApplyLanguage();
            }

            VerifyMainFormShortcutClick(false, "Copie enregistrée dans Documents", true);
            VerifyMainFormShortcutClick(true, "Disque plein", false);
        }

        private static void IsInstalledRequiresMatchingDocumentsCopyAndDesktopShortcut()
        {
            string sourceDir = CreateTempDir("AvIpSwitch-src");
            string docsDir = CreateTempDir("AvIpSwitch-docs");
            string desktopDir = CreateTempDir("AvIpSwitch-desk");
            try
            {
                Assert(!AppShortcut.IsInstalled(docsDir, desktopDir),
                    "Fresh folders must not report shortcut as installed.");
                Assert(!AppShortcut.IsInstalled(Path.Combine(docsDir, "missing"), desktopDir),
                    "Missing Documents directory must fail open (return false).");
                Assert(!AppShortcut.IsInstalled(docsDir, Path.Combine(desktopDir, "missing")),
                    "Missing Desktop directory must fail open (return false).");

                string docsExe = Path.Combine(docsDir, "AV IP-Switch v1.4.exe");
                File.WriteAllBytes(docsExe, new byte[] { 1, 2, 3 });
                Assert(!AppShortcut.IsInstalled(docsDir, desktopDir),
                    "Having only the Documents copy without a Desktop shortcut must keep the button visible.");

                string oldDocsExe = Path.Combine(docsDir, "AV IP-Switch v1.2.1.exe");
                File.WriteAllBytes(oldDocsExe, new byte[] { 1, 2, 3 });
                string linkPath = Path.Combine(desktopDir, "AV IP-Switch.lnk");
                object shell = new ShellLinkCom();
                try
                {
                    IShellLinkW link = (IShellLinkW)shell;
                    link.SetPath(oldDocsExe);
                    link.SetWorkingDirectory(docsDir);
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)shell).Save(linkPath, true);
                }
                finally
                {
                    if (System.Runtime.InteropServices.Marshal.IsComObject(shell))
                    {
                        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
                    }
                }
                Assert(!AppShortcut.IsInstalled(docsDir, desktopDir),
                    "A shortcut pointing to an older version must keep the button visible.");

                string sourceExe = Path.Combine(sourceDir, "AV IP-Switch.exe");
                File.WriteAllBytes(sourceExe, new byte[] { 4, 5, 6 });
                AppShortcutResult installed = AppShortcut.Install(sourceExe, docsDir, desktopDir, null);
                Assert(installed.Success, "Install must update the shortcut to v1.4.");
                Assert(AppShortcut.IsInstalled(docsDir, desktopDir),
                    "IsInstalled must return true once both v1.4 copy and matching shortcut exist.");
            }
            finally
            {
                DeleteDir(sourceDir);
                DeleteDir(docsDir);
                DeleteDir(desktopDir);
            }
        }

        private static void ShowHidden(MainForm form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new System.Drawing.Point(-32000, -32000);
            form.ShowInTaskbar = false;
            form.Show();
        }

        private static void MainFormHidesShortcutButtonWhenInstalledAndShowsOnFailure()
        {
            FakeAppShortcutBackend installedBackend = new FakeAppShortcutBackend(false);
            installedBackend.Installed = true;
            using (MainForm form = new MainForm(null, installedBackend))
            {
                ShowHidden(form);
                FlatButton btnShortcut = Field<FlatButton>(form, "_btnShortcut");
                FlatButton btnConnections = Field<FlatButton>(form, "_btnConnections");
                TabBar tabs = Field<TabBar>(form, "_tabs");

                Assert(!btnShortcut.Visible, "Shortcut button must be hidden when already installed.");
                Assert(btnConnections.Visible && tabs.Right <= btnConnections.Left,
                    "Tabs and Connections button must remain properly laid out when shortcut button is hidden.");
            }

            FakeAppShortcutBackend failingCheckBackend = new FakeAppShortcutBackend(false);
            failingCheckBackend.ThrowOnIsInstalled = true;
            using (MainForm form = new MainForm(null, failingCheckBackend))
            {
                ShowHidden(form);
                FlatButton btnShortcut = Field<FlatButton>(form, "_btnShortcut");

                Assert(btnShortcut.Visible,
                    "Shortcut button must remain visible by default if installation check fails.");
            }
        }

        private static void VerifyMainFormShortcutClick(bool shouldFail, string expectedStatus, bool expectSuccessColor)
        {
            FakeAppShortcutBackend backend = new FakeAppShortcutBackend(shouldFail);
            Exception failure = null;
            bool completed = false;
            using (MainForm form = new MainForm(null, backend))
            using (ApplicationContext context = new ApplicationContext())
            using (System.Windows.Forms.Timer timeout = new System.Windows.Forms.Timer())
            {
                ShowHidden(form);
                FlatButton btnShortcut = Field<FlatButton>(form, "_btnShortcut");
                Label status = Field<Label>(form, "_lblStatus");
                status.TextChanged += delegate
                {
                    if (status.Text.IndexOf(expectedStatus, StringComparison.Ordinal) < 0)
                    {
                        return;
                    }
                    try
                    {
                        Assert(backend.Calls == 1, "Shortcut button must invoke backend once.");
                        Assert(backend.Apartment == ApartmentState.STA, "COM shortcut backend must run on an STA thread.");
                        Assert(btnShortcut.Enabled && !Field<bool>(form, "_appShortcutBusy"),
                            "Shortcut button must be re-enabled after completion.");
                        Assert(btnShortcut.Visible == shouldFail,
                            shouldFail
                                ? "Shortcut button must remain visible when creation fails."
                                : "Shortcut button must disappear immediately after successful creation.");
                        Assert(status.ForeColor == (expectSuccessColor ? Theme.Success : Theme.Danger),
                            "Status color must match outcome.");
                        completed = true;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    context.ExitThread();
                };

                timeout.Interval = 10000;
                timeout.Tick += delegate
                {
                    timeout.Stop();
                    failure = new InvalidOperationException("AppShortcut button test timed out.");
                    context.ExitThread();
                };
                timeout.Start();

                form.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        MethodInfo onClick = btnShortcut.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic);
                        onClick.Invoke(btnShortcut, new object[] { EventArgs.Empty });
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        context.ExitThread();
                    }
                }));

                Application.Run(context);
                timeout.Stop();
            }

            if (failure != null)
            {
                throw new InvalidOperationException("AppShortcut MainForm test failed.", failure);
            }
            Assert(completed, "Shortcut button test must observe status update.");
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private sealed class FakeAppShortcutBackend : IAppShortcutBackend
        {
            private readonly bool _fail;
            public bool Installed;
            public bool ThrowOnIsInstalled;
            public int Calls;
            public ApartmentState Apartment;

            public FakeAppShortcutBackend(bool fail)
            {
                _fail = fail;
            }

            public bool IsInstalled()
            {
                if (ThrowOnIsInstalled)
                {
                    throw new UnauthorizedAccessException("Acces refuse");
                }
                return Installed;
            }

            public AppShortcutResult CreateCopyAndShortcut()
            {
                Calls++;
                Apartment = Thread.CurrentThread.GetApartmentState();
                AppShortcutResult result = new AppShortcutResult();
                if (_fail)
                {
                    result.CopyError = "Disque plein";
                    return result;
                }
                result.CopyPath = @"C:\Users\Demo\Documents\AV IP-Switch v1.4.exe";
                result.Copied = true;
                result.ShortcutPath = @"C:\Users\Demo\Desktop\AV IP-Switch.lnk";
                result.ShortcutCreated = true;
                return result;
            }
        }
    }
}
