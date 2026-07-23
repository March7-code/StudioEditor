using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace BodyEditor.UI
{
    internal static class WindowsModelFilePicker
    {
        private static string lastDirectory;

        public static bool TryPick(
            IReadOnlyList<string> extensions,
            out string filePath,
            out string error,
            string dialogTitle = "Import Model",
            string filterLabel = "Supported Models")
        {
            filePath = string.Empty;
            error = string.Empty;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            IntPtr filter = IntPtr.Zero;
            IntPtr fileBuffer = IntPtr.Zero;
            IntPtr initialDirectory = IntPtr.Zero;
            IntPtr title = IntPtr.Zero;
            IntPtr defaultExtension = IntPtr.Zero;

            try
            {
                const int maxFileCharacters = 4096;
                filter = Marshal.StringToHGlobalUni(
                    BuildFilter(extensions, filterLabel));
                fileBuffer = Marshal.AllocHGlobal(maxFileCharacters * sizeof(char));
                Marshal.WriteInt16(fileBuffer, 0);
                initialDirectory = AllocateString(lastDirectory);
                title = Marshal.StringToHGlobalUni(dialogTitle);
                defaultExtension = AllocateString(GetDefaultExtension(extensions));

                var options = new OpenFileName
                {
                    structSize = Marshal.SizeOf<OpenFileName>(),
                    owner = GetForegroundWindow(),
                    filter = filter,
                    filterIndex = 1,
                    file = fileBuffer,
                    maxFile = maxFileCharacters,
                    initialDirectory = initialDirectory,
                    title = title,
                    flags = FileMustExist | PathMustExist | Explorer | NoChangeDirectory,
                    defaultExtension = defaultExtension,
                };

                if (!GetOpenFileName(ref options))
                {
                    var dialogError = CommDlgExtendedError();
                    if (dialogError != 0)
                    {
                        error = $"Windows file dialog failed (0x{dialogError:X}).";
                    }

                    return false;
                }

                filePath = Marshal.PtrToStringUni(fileBuffer) ?? string.Empty;
                lastDirectory = Path.GetDirectoryName(filePath);
                return !string.IsNullOrEmpty(filePath);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
            finally
            {
                Free(filter);
                Free(fileBuffer);
                Free(initialDirectory);
                Free(title);
                Free(defaultExtension);
            }
#else
            error = "The runtime file picker is currently available on Windows only.";
            return false;
#endif
        }

        public static bool TryPickDirectory(
            out string directoryPath,
            out string error,
            string dialogTitle = "Select Directory",
            string initialDirectory = null)
        {
            directoryPath = string.Empty;
            error = string.Empty;

#if UNITY_EDITOR
            try
            {
                var startDirectory = Directory.Exists(initialDirectory)
                    ? initialDirectory
                    : Directory.Exists(lastDirectory)
                        ? lastDirectory
                        : string.Empty;
                directoryPath = UnityEditor.EditorUtility.OpenFolderPanel(
                    dialogTitle,
                    startDirectory,
                    string.Empty);
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    directoryPath = string.Empty;
                    return false;
                }

                directoryPath = Path.GetFullPath(directoryPath);
                lastDirectory = directoryPath;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
#else
            error = "The directory picker is available in the Unity Editor only.";
            return false;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int FileMustExist = 0x00001000;
        private const int PathMustExist = 0x00000800;
        private const int Explorer = 0x00080000;
        private const int NoChangeDirectory = 0x00000008;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int structSize;
            public IntPtr owner;
            public IntPtr instance;
            public IntPtr filter;
            public IntPtr customFilter;
            public int maxCustomFilter;
            public int filterIndex;
            public IntPtr file;
            public int maxFile;
            public IntPtr fileTitle;
            public int maxFileTitle;
            public IntPtr initialDirectory;
            public IntPtr title;
            public int flags;
            public ushort fileOffset;
            public ushort fileExtension;
            public IntPtr defaultExtension;
            public IntPtr customData;
            public IntPtr hook;
            public IntPtr templateName;
            public IntPtr reserved;
            public int reservedValue;
            public int flagsEx;
        }

        [DllImport(
            "comdlg32.dll",
            EntryPoint = "GetOpenFileNameW",
            CharSet = CharSet.Unicode,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetOpenFileName(ref OpenFileName options);

        [DllImport("comdlg32.dll")]
        private static extern int CommDlgExtendedError();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static IntPtr AllocateString(string value)
        {
            return string.IsNullOrEmpty(value)
                ? IntPtr.Zero
                : Marshal.StringToHGlobalUni(value);
        }

        private static void Free(IntPtr value)
        {
            if (value != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(value);
            }
        }

        private static string BuildFilter(
            IReadOnlyList<string> extensions,
            string label)
        {
            var patterns = new List<string>();
            for (var index = 0; index < extensions.Count; index++)
            {
                var extension = extensions[index];
                if (!string.IsNullOrWhiteSpace(extension))
                {
                    patterns.Add("*" + NormalizeExtension(extension));
                }
            }

            var joinedPatterns = patterns.Count > 0
                ? string.Join(";", patterns)
                : "*.*";
            label = string.IsNullOrWhiteSpace(label)
                ? "Supported Files"
                : label;
            return $"{label} ({joinedPatterns})\0{joinedPatterns}\0" +
                   "All Files (*.*)\0*.*\0\0";
        }

        private static string GetDefaultExtension(IReadOnlyList<string> extensions)
        {
            return extensions.Count == 0
                ? string.Empty
                : NormalizeExtension(extensions[0]).TrimStart('.');
        }

        private static string NormalizeExtension(string extension)
        {
            return extension.StartsWith(".", StringComparison.Ordinal)
                ? extension
                : "." + extension;
        }
#endif
    }
}
