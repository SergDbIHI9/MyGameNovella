using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EngineEditor.Models;

public enum EditorAssetType
{
    All,
    Image,
    Audio,
    Other
}

public sealed record EditorAssetInfo(string RelativePath, string FullPath, long Length, DateTime LastModifiedUtc, EditorAssetType Type);

public static class EditorAssetBrowser
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".ogg", ".flac", ".mp3" };

    public static EditorAssetType GetType(string path)
    {
        string extension = Path.GetExtension(path);
        if (ImageExtensions.Contains(extension))
            return EditorAssetType.Image;
        if (AudioExtensions.Contains(extension))
            return EditorAssetType.Audio;
        return EditorAssetType.Other;
    }

    public static IReadOnlyList<EditorAssetInfo> Find(string assetsDirectory, string projectDirectory,
        string? query = null, EditorAssetType type = EditorAssetType.All)
    {
        if (!Directory.Exists(assetsDirectory))
            return Array.Empty<EditorAssetInfo>();

        return Directory.EnumerateFiles(assetsDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(file => type == EditorAssetType.All || GetType(file.FullName) == type)
            .Where(file => string.IsNullOrWhiteSpace(query)
                || Path.GetRelativePath(projectDirectory, file.FullName)
                    .Contains(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(file => new EditorAssetInfo(
                Path.GetRelativePath(projectDirectory, file.FullName).Replace(Path.DirectorySeparatorChar, '/'),
                file.FullName, file.Length, file.LastWriteTimeUtc, GetType(file.FullName)))
            .ToArray();
    }
}

public static class EditorSceneSearch
{
    public static bool Matches(SceneModel scene, string? query)
    {
        if (scene is null)
            throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(query))
            return true;
        string value = query.Trim();
        return scene.Name.Contains(value, StringComparison.OrdinalIgnoreCase)
            || (scene.Text?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
