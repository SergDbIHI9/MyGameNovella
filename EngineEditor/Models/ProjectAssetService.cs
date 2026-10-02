using System;
using System.IO;

namespace EngineEditor.Models;

public static class ProjectAssetService
{
    public static string Import(string sourceFile, string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        string sourcePath = Path.GetFullPath(sourceFile);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The selected asset does not exist.", sourcePath);

        string assetsDirectory = Path.Combine(Path.GetFullPath(projectDirectory), "Assets");
        Directory.CreateDirectory(assetsDirectory);

        string fileName = Path.GetFileName(sourcePath);
        string destinationPath = Path.Combine(assetsDirectory, fileName);
        string extension = Path.GetExtension(fileName);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        for (int suffix = 2; File.Exists(destinationPath); suffix++)
            destinationPath = Path.Combine(assetsDirectory, $"{nameWithoutExtension}-{suffix}{extension}");

        File.Copy(sourcePath, destinationPath);
        return Path.GetRelativePath(Path.GetFullPath(projectDirectory), destinationPath)
            .Replace(Path.DirectorySeparatorChar, '/');
    }
}
