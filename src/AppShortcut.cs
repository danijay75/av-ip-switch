using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows.Forms;

namespace EasyIpSwitch
{
    internal sealed class AppShortcutResult
    {
        public string CopyPath = "";
        public bool Copied;
        public bool AlreadyAtDestination;
        public string ShortcutPath = "";
        public bool ShortcutCreated;
        public bool ShortcutUpdated;
        public string CopyError = "";
        public string ShortcutError = "";

        public bool Success
        {
            get
            {
                return CopyPath.Length > 0
                    && ShortcutPath.Length > 0
                    && CopyError.Length == 0
                    && ShortcutError.Length == 0;
            }
        }

        public string Message
        {
            get
            {
                if (!string.IsNullOrEmpty(CopyError))
                {
                    return L.F("Impossible de copier l'application dans Documents : {0}", CopyError);
                }

                string copyName = Path.GetFileName(CopyPath);
                string shortcutName = Path.GetFileNameWithoutExtension(ShortcutPath);

                if (!string.IsNullOrEmpty(ShortcutError))
                {
                    return AlreadyAtDestination
                        ? L.F("L'application est déjà dans Documents (« {0} »), mais le raccourci n'a pas pu être créé : {1}", copyName, ShortcutError)
                        : L.F("Copie créée dans Documents (« {0} »), mais le raccourci n'a pas pu être créé : {1}", copyName, ShortcutError);
                }

                if (AlreadyAtDestination)
                {
                    if (ShortcutCreated)
                    {
                        return L.F("Application déjà dans Documents (« {0} ») ; raccourci « {1} » créé sur le Bureau.", copyName, shortcutName);
                    }
                    if (ShortcutUpdated)
                    {
                        return L.F("Application déjà dans Documents (« {0} ») ; raccourci « {1} » mis à jour sur le Bureau.", copyName, shortcutName);
                    }
                    return L.F("Application déjà dans Documents (« {0} ») ; raccourci « {1} » déjà à jour sur le Bureau.", copyName, shortcutName);
                }

                if (ShortcutCreated)
                {
                    return L.F("Copie enregistrée dans Documents (« {0} ») ; raccourci « {1} » créé sur le Bureau.", copyName, shortcutName);
                }
                if (ShortcutUpdated)
                {
                    return L.F("Copie enregistrée dans Documents (« {0} ») ; raccourci « {1} » mis à jour sur le Bureau.", copyName, shortcutName);
                }
                return L.F("Copie enregistrée dans Documents (« {0} ») ; raccourci « {1} » déjà à jour sur le Bureau.", copyName, shortcutName);
            }
        }
    }

    internal interface IAppShortcutBackend
    {
        bool IsInstalled();
        AppShortcutResult CreateCopyAndShortcut();
    }

    internal sealed class WindowsAppShortcutBackend : IAppShortcutBackend
    {
        public bool IsInstalled()
        {
            try
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return AppShortcut.IsInstalled(documents, desktop);
            }
            catch
            {
                return false;
            }
        }

        public AppShortcutResult CreateCopyAndShortcut()
        {
            string sourceExe = Application.ExecutablePath;
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return AppShortcut.Install(sourceExe, documents, desktop, NetworkConnectionsShortcut.NotifyDesktop);
        }
    }

    internal static class AppShortcut
    {
        internal static bool CheckInstalled(IAppShortcutBackend backend)
        {
            if (backend == null)
            {
                return false;
            }
            try
            {
                return backend.IsInstalled();
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsInstalled(string documentsDirectory, string desktopDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(documentsDirectory) || !Directory.Exists(documentsDirectory))
                {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(desktopDirectory) || !Directory.Exists(desktopDirectory))
                {
                    return false;
                }

                string targetExePath = Path.Combine(documentsDirectory, CopyFileName());
                if (!File.Exists(targetExePath))
                {
                    return false;
                }

                string[] links = Directory.GetFiles(desktopDirectory, AppInfo.ProductName + "*.lnk");
                for (int i = 0; i < links.Length; i++)
                {
                    string existingTarget;
                    if (TryReadShortcutTarget(links[i], out existingTarget) && PathsEqual(existingTarget, targetExePath))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static AppShortcutResult Execute()
        {
            return Execute(new WindowsAppShortcutBackend());
        }

        internal static AppShortcutResult Execute(IAppShortcutBackend backend)
        {
            try
            {
                AppShortcutResult result = backend.CreateCopyAndShortcut();
                if (result == null)
                {
                    AppShortcutResult fallback = new AppShortcutResult();
                    fallback.CopyError = L.T("Opération non confirmée.");
                    return fallback;
                }
                return result;
            }
            catch (Exception ex)
            {
                AppShortcutResult error = new AppShortcutResult();
                error.CopyError = Describe(ex);
                return error;
            }
        }

        internal static string CopyFileName()
        {
            return AppInfo.ProductName + " " + AppInfo.VersionLabel + ".exe";
        }

        internal static AppShortcutResult Install(
            string sourceExePath,
            string documentsDirectory,
            string desktopDirectory,
            Action<string, bool> notifyDesktop)
        {
            AppShortcutResult result = new AppShortcutResult();

            if (string.IsNullOrWhiteSpace(sourceExePath) || !File.Exists(sourceExePath))
            {
                result.CopyError = L.T("Exécutable source introuvable.");
                return result;
            }
            if (string.IsNullOrWhiteSpace(documentsDirectory) || !Directory.Exists(documentsDirectory))
            {
                result.CopyError = L.T("Le dossier Documents est introuvable.");
                return result;
            }

            string targetExePath = Path.Combine(documentsDirectory, CopyFileName());
            if (PathsEqual(sourceExePath, targetExePath))
            {
                result.CopyPath = targetExePath;
                result.AlreadyAtDestination = true;
            }
            else
            {
                try
                {
                    CopyExecutableAtomically(sourceExePath, documentsDirectory, targetExePath);
                    result.CopyPath = targetExePath;
                    result.Copied = true;
                }
                catch (IOException ex)
                {
                    if (File.Exists(targetExePath))
                    {
                        result.CopyError = L.F(
                            "Impossible de remplacer « {0} » (fermez l'autre fenêtre d'AV IP-Switch exécutée depuis Documents puis réessayez).",
                            CopyFileName());
                    }
                    else
                    {
                        result.CopyError = Describe(ex);
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    result.CopyError = Describe(ex);
                    return result;
                }
            }

            try
            {
                EnsureDesktopShortcut(targetExePath, documentsDirectory, desktopDirectory, notifyDesktop, result);
            }
            catch (Exception ex)
            {
                result.ShortcutError = Describe(ex);
            }

            return result;
        }

        private static void CopyExecutableAtomically(string sourceExePath, string documentsDirectory, string targetExePath)
        {
            string temp = Path.Combine(documentsDirectory, ".AvIpSwitch-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(sourceExePath, temp, true);
                if (File.Exists(targetExePath))
                {
                    File.Copy(temp, targetExePath, true);
                }
                else
                {
                    File.Move(temp, targetExePath);
                    temp = null;
                }
            }
            finally
            {
                if (temp != null && File.Exists(temp))
                {
                    try
                    {
                        File.Delete(temp);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void EnsureDesktopShortcut(
            string targetExePath,
            string workingDirectory,
            string desktopDirectory,
            Action<string, bool> notifyDesktop,
            AppShortcutResult result)
        {
            if (string.IsNullOrWhiteSpace(desktopDirectory) || !Directory.Exists(desktopDirectory))
            {
                throw new DirectoryNotFoundException(L.T("Le dossier Bureau est introuvable."));
            }

            for (int i = 0; i < 1000; i++)
            {
                string candidate = Path.Combine(desktopDirectory, CandidateName(i));
                if (Directory.Exists(candidate))
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    string existingTarget;
                    if (!TryReadShortcutTarget(candidate, out existingTarget))
                    {
                        continue;
                    }

                    if (PathsEqual(existingTarget, targetExePath))
                    {
                        result.ShortcutPath = candidate;
                        Notify(notifyDesktop, candidate, false);
                        return;
                    }

                    if (IsOwnAppExecutablePath(existingTarget))
                    {
                        WriteShortcut(candidate, targetExePath, workingDirectory);
                        result.ShortcutPath = candidate;
                        result.ShortcutUpdated = true;
                        Notify(notifyDesktop, candidate, false);
                        return;
                    }

                    continue;
                }

                string temp = Path.Combine(desktopDirectory, ".AvIpSwitch-lnk-" + Guid.NewGuid().ToString("N") + ".lnk");
                try
                {
                    using (FileStream reservation = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                    }
                    WriteShortcut(temp, targetExePath, workingDirectory);
                    File.Move(temp, candidate);
                    temp = null;
                    result.ShortcutPath = candidate;
                    result.ShortcutCreated = true;
                    Notify(notifyDesktop, candidate, true);
                    return;
                }
                catch (IOException)
                {
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        continue;
                    }
                    throw;
                }
                finally
                {
                    if (temp != null && File.Exists(temp))
                    {
                        try
                        {
                            File.Delete(temp);
                        }
                        catch
                        {
                        }
                    }
                }
            }

            throw new IOException(L.T("Aucun nom libre pour le raccourci sur le Bureau."));
        }

        private static string CandidateName(int index)
        {
            if (index == 0)
            {
                return AppInfo.ProductName + ".lnk";
            }
            return AppInfo.ProductName + " (" + (index + 1) + ").lnk";
        }

        internal static bool IsOwnAppExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            string fileName = Path.GetFileName(path.Trim());
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return fileName.StartsWith("AV IP-Switch", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Easy IP-Switch", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("av-ip-switch", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("easy-ip-switch", StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }
            try
            {
                return string.Equals(
                    Path.GetFullPath(a.Trim()),
                    Path.GetFullPath(b.Trim()),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void WriteShortcut(string shortcutPath, string targetExePath, string workingDirectory)
        {
            object shell = new ShellLinkCom();
            try
            {
                IShellLinkW link = (IShellLinkW)shell;
                link.SetPath(targetExePath);
                link.SetArguments("");
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(L.T("Lancer AV IP-Switch"));
                link.SetIconLocation(targetExePath, 0);
                ((IPersistFile)shell).Save(shortcutPath, true);
            }
            finally
            {
                if (Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        internal static bool TryReadShortcutTarget(string shortcutPath, out string targetPath)
        {
            targetPath = "";
            object shell = null;
            try
            {
                shell = new ShellLinkCom();
                ((IPersistFile)shell).Load(shortcutPath, 0);
                IShellLinkW link = (IShellLinkW)shell;
                StringBuilder target = new StringBuilder(32768);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
                targetPath = target.ToString();
                return targetPath.Length > 0;
            }
            catch
            {
                targetPath = "";
                return false;
            }
            finally
            {
                if (shell != null && Marshal.IsComObject(shell))
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
        }

        private static void Notify(Action<string, bool> notifyDesktop, string path, bool created)
        {
            if (notifyDesktop == null)
            {
                return;
            }
            try
            {
                notifyDesktop(path, created);
            }
            catch
            {
            }
        }

        private static string Describe(Exception exception)
        {
            Exception current = exception;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }
            string message = (current.Message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (message.Length == 0)
            {
                return L.T("Erreur Windows inconnue.");
            }
            return message.Length > 180 ? message.Substring(0, 180) + "…" : message;
        }
    }
}
