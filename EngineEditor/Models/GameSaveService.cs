using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EngineEditor.Models;

public sealed class GameSaveData
{
    public int FormatVersion { get; set; } = 1;
    public string SceneId { get; set; } = "";
    public Dictionary<string, float> Variables { get; set; } = new(StringComparer.Ordinal);
    public List<string> SeenSceneIds { get; set; } = new();
    public DateTimeOffset SavedAtUtc { get; set; }
}

public static class GameSaveService
{
    public const int SlotCount = 6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string GetDefaultSavePath(string projectFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFilePath);
        string fullPath = Path.GetFullPath(projectFilePath);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("The local application data directory is unavailable.");

        string projectName = Path.GetFileNameWithoutExtension(fullPath);
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            projectName = projectName.Replace(invalidCharacter, '_');
        if (string.IsNullOrWhiteSpace(projectName))
            projectName = "Game";

        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant()))[..8])
            .ToLowerInvariant();
        return Path.Combine(localAppData, "MyGameNovella", "Saves", $"{projectName}-{identity}.json");
    }

    public static string GetSlotSavePath(string projectFilePath, int slotIndex)
    {
        if (slotIndex is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));

        string defaultPath = GetDefaultSavePath(projectFilePath);
        return slotIndex == 0
            ? defaultPath
            : Path.Combine(
                Path.GetDirectoryName(defaultPath)!,
                $"{Path.GetFileNameWithoutExtension(defaultPath)}-slot-{slotIndex + 1}.json");
    }

    public static void Save(
        string saveFilePath,
        string sceneId,
        IReadOnlyDictionary<string, float> variables,
        IEnumerable<string>? seenSceneIds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);

        var copiedVariables = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            if (string.IsNullOrWhiteSpace(name) || !float.IsFinite(value))
                throw new InvalidDataException("Game saves require named variables with finite values.");
            copiedVariables.Add(name, value);
        }

        var data = new GameSaveData
        {
            SceneId = sceneId,
            Variables = copiedVariables,
            SeenSceneIds = seenSceneIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList()
                ?? new List<string>(),
            SavedAtUtc = DateTimeOffset.UtcNow
        };
        string fullPath = Path.GetFullPath(saveFilePath);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The save file path has no parent directory.");
        Directory.CreateDirectory(directory);

        string temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(data, JsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static GameSaveData Load(string saveFilePath, IEnumerable<SceneModel> scenes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(scenes);

        string json = File.ReadAllText(saveFilePath, Encoding.UTF8);
        GameSaveData data;
        try
        {
            data = JsonSerializer.Deserialize<GameSaveData>(json, JsonOptions)
                ?? throw new InvalidDataException("The save file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The save file is malformed: {saveFilePath}", ex);
        }

        if (data.FormatVersion != 1)
            throw new InvalidDataException($"Unsupported save format version {data.FormatVersion}.");
        if (string.IsNullOrWhiteSpace(data.SceneId))
            throw new InvalidDataException("The save file does not contain a scene ID.");
        if (data.Variables is null || data.Variables.Any(entry =>
                string.IsNullOrWhiteSpace(entry.Key) || !float.IsFinite(entry.Value)))
            throw new InvalidDataException("The save file contains invalid game variables.");
        data.SeenSceneIds ??= new List<string>();
        if (data.SeenSceneIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("The save file contains invalid scene history.");
        if (!scenes.Any(scene => scene.Id == data.SceneId))
            throw new InvalidDataException($"The saved scene '{data.SceneId}' no longer exists in this project.");

        return data;
    }
}
