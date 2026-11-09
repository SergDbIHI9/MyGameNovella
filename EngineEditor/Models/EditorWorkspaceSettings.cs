using System;
using System.IO;
using System.Text.Json;

namespace EngineEditor.Models;

public sealed class EditorWorkspaceSettings
{
    public double WindowWidth { get; set; } = 1600;
    public double WindowHeight { get; set; } = 960;
    public int PositionX { get; set; } = int.MinValue;
    public int PositionY { get; set; } = int.MinValue;
    public double HierarchyWidth { get; set; } = 242;
    public double InspectorWidth { get; set; } = 360;
    public double BottomHeight { get; set; } = 190;
    public int InspectorTab { get; set; }
    public int BottomTab { get; set; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MyGameNovella", "editor-workspace.json");

    public static EditorWorkspaceSettings Load(string? path = null, Action<string>? onError = null)
    {
        try
        {
            string file = path ?? DefaultPath;
            if (!File.Exists(file))
                return new EditorWorkspaceSettings();
            var settings = JsonSerializer.Deserialize<EditorWorkspaceSettings>(File.ReadAllText(file),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (settings is null)
                throw new InvalidDataException("Workspace settings are empty.");
            if (!settings.IsValid())
            {
                onError?.Invoke("Workspace settings contain invalid dimensions.");
                return new EditorWorkspaceSettings();
            }
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or InvalidDataException)
        {
            onError?.Invoke(ex.Message);
            return new EditorWorkspaceSettings();
        }
    }

    public void Save(string? path = null)
    {
        string file = path ?? DefaultPath;
        string directory = Path.GetDirectoryName(file)
            ?? throw new InvalidOperationException("Workspace settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        string temporaryFile = file + ".tmp";
        File.WriteAllText(temporaryFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporaryFile, file, overwrite: true);
    }

    public void ClampToAvailableSize(double availableWidth, double availableHeight)
    {
        if (!double.IsFinite(availableWidth) || !double.IsFinite(availableHeight)
            || availableWidth <= 0 || availableHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth), "Available screen dimensions must be finite and positive.");

        WindowWidth = Math.Clamp(WindowWidth, Math.Min(1120, availableWidth), availableWidth);
        WindowHeight = Math.Clamp(WindowHeight, Math.Min(720, availableHeight), availableHeight);
    }

    public bool IsValid() =>
        double.IsFinite(WindowWidth) && WindowWidth is >= 1120 and <= 10000 &&
        double.IsFinite(WindowHeight) && WindowHeight is >= 720 and <= 10000 &&
        ((PositionX == int.MinValue && PositionY == int.MinValue)
         || (PositionX is >= -100000 and <= 100000 && PositionY is >= -100000 and <= 100000)) &&
        double.IsFinite(HierarchyWidth) && HierarchyWidth is >= 160 and <= 1000 &&
        double.IsFinite(InspectorWidth) && InspectorWidth is >= 240 and <= 1200 &&
        double.IsFinite(BottomHeight) && BottomHeight is >= 100 and <= 900 &&
        InspectorTab is >= 0 and <= 1 && BottomTab is >= 0 and <= 1;
}
