using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SprocketPartClipboard.Clipboard
{
    public sealed class ClipboardLoadResult
    {
        public ClipboardLoadResult(ClipboardLibrary library, string? backupPath, string? error)
        {
            Library = library;
            BackupPath = backupPath;
            Error = error;
        }

        public ClipboardLibrary Library { get; }

        // 损坏文件被改名保留的位置；没有损坏时为 null。
        public string? BackupPath { get; }

        public string? Error { get; }

        public bool RecoveredFromCorruption => BackupPath != null;
    }

    public sealed class ClipboardLibraryStore
    {
        public const string FileName = "library.json";
        public const string UserDataFolderName = "SprocketPartClipboard";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        private readonly string filePath;

        public ClipboardLibraryStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A library file path is required.", nameof(filePath));

            this.filePath = Path.GetFullPath(filePath);
        }

        public string FilePath => filePath;

        public static string DefaultFilePath(string userDataDirectory)
        {
            if (string.IsNullOrWhiteSpace(userDataDirectory))
                throw new ArgumentException("A user data directory is required.", nameof(userDataDirectory));

            return Path.Combine(userDataDirectory, UserDataFolderName, FileName);
        }

        public ClipboardLoadResult Load()
        {
            if (!File.Exists(filePath))
                return new ClipboardLoadResult(new ClipboardLibrary(), null, null);

            string json;
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // 读不到不等于内容坏了：文件保持原样，本次会话从空库开始。
                return new ClipboardLoadResult(new ClipboardLibrary(), null, exception.Message);
            }

            try
            {
                ClipboardLibrary? library = JsonSerializer.Deserialize<ClipboardLibrary>(json, SerializerOptions);
                if (library == null)
                    throw new JsonException("The library document is empty.");

                library.Normalize();
                return new ClipboardLoadResult(library, null, null);
            }
            catch (JsonException exception)
            {
                string backupPath = BackupCorruptFile();
                return new ClipboardLoadResult(new ClipboardLibrary(), backupPath, exception.Message);
            }
        }

        public void Save(ClipboardLibrary library)
        {
            if (library == null)
                throw new ArgumentNullException(nameof(library));

            library.Normalize();

            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // 先写同目录临时文件再整体替换：中途崩溃不会留下半截库，也不会出现只剩临时文件的状态。
            string temporaryPath = filePath + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(library, SerializerOptions));
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private string BackupCorruptFile()
        {
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string directory = Path.GetDirectoryName(filePath) ?? ".";
            string candidate = Path.Combine(directory, $"library.corrupt-{stamp}.json");

            int suffix = 1;
            while (File.Exists(candidate))
            {
                candidate = Path.Combine(directory, $"library.corrupt-{stamp}-{suffix}.json");
                suffix++;
            }

            File.Move(filePath, candidate);
            return candidate;
        }
    }
}
