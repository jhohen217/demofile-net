using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class ShellMetadata
{
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, out WIN32_FIND_DATA pfd, SLGP_FLAGS fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, SLR_FLAGS fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [Flags]
    private enum SLGP_FLAGS
    {
        SLGP_SHORTPATH = 0x1,
        SLGP_UNCPRIORITY = 0x2,
        SLGP_RAWPATH = 0x4
    }

    [Flags]
    private enum SLR_FLAGS
    {
        SLR_NO_UI = 0x1,
        SLR_ANY_MATCH = 0x2,
        SLR_UPDATE = 0x4,
        SLR_NOUPDATE = 0x8,
        SLR_NOSEARCH = 0x10,
        SLR_NOTRACK = 0x20,
        SLR_NOLINKINFO = 0x40,
        SLR_INVOKE_MSI = 0x80
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    private static void SetFileMetadata(string filePath, string title, string comment, string category, string[] tags)
    {
        try
        {
            // Create shortcut to set metadata
            var shellLink = (IShellLink)new ShellLink();
            var persistFile = (IPersistFile)shellLink;

            // Set the path to the target file
            shellLink.SetPath(filePath);
            shellLink.SetDescription(comment);  // This sets the comment/tooltip

            // Create a temporary .lnk file to store metadata
            var tempLinkPath = Path.Combine(
                Path.GetDirectoryName(filePath),
                Path.GetFileNameWithoutExtension(filePath) + ".lnk"
            );

            try
            {
                // Save the shortcut
                persistFile.Save(tempLinkPath, true);

                // Set additional properties through the shortcut
                using (var writer = new StreamWriter(tempLinkPath, true))
                {
                    writer.WriteLine($"Title={title}");
                    writer.WriteLine($"Category={category}");
                    writer.WriteLine($"Tags={string.Join(";", tags)}");
                }
            }
            finally
            {
                // Clean up
                if (File.Exists(tempLinkPath))
                {
                    File.Delete(tempLinkPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not set file metadata: {ex.Message}");
        }
    }

    public static void SetKillCollectionMetadata(string filePath, string mapName, int aceCount, int quadCount, int tripleCount, int multiKillCount, int totalTicks)
    {
        var title = $"Kill Collection - {mapName}";
        var comment = $"Map: {mapName}\n" +
                     $"Aces: {aceCount}\n" +
                     $"Quads: {quadCount}\n" +
                     $"Triples: {tripleCount}\n" +
                     $"Multi-Kills: {multiKillCount}\n" +
                     $"Total Ticks: {totalTicks}";
        var category = "Kill Collection";
        var tags = new[]
        {
            "CSGO",
            mapName,
            "Kills",
            aceCount > 0 ? "Ace" : "",
            quadCount > 0 ? "Quad" : "",
            tripleCount > 0 ? "Triple" : "",
            multiKillCount > 0 ? "Multi" : ""
        };

        SetFileMetadata(filePath, title, comment, category, tags);
    }

    public static void SetPOVMetadata(string filePath, string playerName, string steamId, string mapName, int killCount, int round, int tickDuration, string type, string weapons, float victimsRadius, float killerRadius, float killerMoveDistance)
    {
        var title = $"{type} - {playerName} - Round {round}";
        var comment = $"Player: {playerName}\n" +
                     $"SteamID: {steamId}\n" +
                     $"Map: {mapName}\n" +
                     $"Round: {round}\n" +
                     $"Type: {type}\n" +
                     $"Kills: {killCount}\n" +
                     $"Duration: {tickDuration} ticks\n" +
                     $"Weapons: {weapons}\n" +
                     $"Victims Spread: {victimsRadius:F2} units\n" +
                     $"Killer Movement Radius: {killerRadius:F2} units\n" +
                     $"Total Distance Moved: {killerMoveDistance:F2} units";
        var category = "POV Data";
        var tags = new[]
        {
            "CSGO",
            mapName,
            playerName,
            steamId,
            $"Round {round}",
            "POV",
            type
        };

        SetFileMetadata(filePath, title, comment, category, tags);
    }
}
