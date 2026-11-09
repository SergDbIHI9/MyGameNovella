using System;
using System.IO;
using EngineEditor.Models;
using Xunit;

namespace EngineEditor.Tests;

public sealed class EditorWorkflowTests
{
    [Fact]
    public void WorkspaceSettingsRoundTripAndRejectInvalidGeometry()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "workspace.json");
        try
        {
            var settings = new EditorWorkspaceSettings { WindowWidth = 1440, InspectorTab = 1 };
            settings.Save(path);

            var loaded = EditorWorkspaceSettings.Load(path);
            Assert.Equal(1440, loaded.WindowWidth);
            Assert.Equal(1, loaded.InspectorTab);

            File.WriteAllText(path, """{"windowWidth":1}""");
            string? loadError = null;
            Assert.Equal(1600, EditorWorkspaceSettings.Load(path, message => loadError = message).WindowWidth);
            Assert.NotNull(loadError);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WorkspaceWindowSizeIsClampedToAvailableLaptopScreen()
    {
        var settings = new EditorWorkspaceSettings { WindowWidth = 1600, WindowHeight = 960 };

        settings.ClampToAvailableSize(1350, 728);

        Assert.Equal(1350, settings.WindowWidth);
        Assert.Equal(728, settings.WindowHeight);
        Assert.True(settings.IsValid());
    }

    [Fact]
    public void UndoHistoryMovesBetweenUndoAndRedoAndHonorsCapacity()
    {
        var history = new EditorUndoHistory<int>(2);
        history.Record(1);
        history.Record(2);
        history.Record(3);

        Assert.True(history.TryUndo(4, out int latest));
        Assert.Equal(3, latest);
        Assert.True(history.TryUndo(latest, out int earlier));
        Assert.Equal(2, earlier);
        Assert.True(history.TryRedo(earlier, out int redoLatest));
        Assert.Equal(3, redoLatest);
        Assert.True(history.TryRedo(redoLatest, out int redoCurrent));
        Assert.Equal(4, redoCurrent);
        Assert.False(history.TryRedo(redoCurrent, out _));
        Assert.True(history.TryUndo(redoCurrent, out int branchPoint));
        history.Record(branchPoint + 10);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void AssetBrowserFiltersByQueryAndType()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string assets = Path.Combine(directory, "Assets");
        Directory.CreateDirectory(assets);
        try
        {
            File.WriteAllText(Path.Combine(assets, "background.png"), "image");
            File.WriteAllText(Path.Combine(assets, "theme.wav"), "audio");
            File.WriteAllText(Path.Combine(assets, "notes.txt"), "text");

            var images = EditorAssetBrowser.Find(assets, directory, "BACK", EditorAssetType.Image);
            Assert.Single(images);
            Assert.Equal("Assets/background.png", images[0].RelativePath);
            Assert.Single(EditorAssetBrowser.Find(assets, directory, type: EditorAssetType.Audio));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SceneSearchMatchesNameAndDialogueTextWithoutChangingSceneData()
    {
        var scene = new SceneModel { Name = "Forest", Text = "Meet the fox" };

        Assert.True(EditorSceneSearch.Matches(scene, "FORE"));
        Assert.True(EditorSceneSearch.Matches(scene, "fox"));
        Assert.False(EditorSceneSearch.Matches(scene, "castle"));
        Assert.Equal("Forest", scene.Name);
    }

    [Fact]
    public void SceneDiagnosticsAreNotSerializedIntoNovelProject()
    {
        var scene = new SceneModel { Name = "Start" };
        scene.SelectedSceneDiagnostics.Add("temporary diagnostic");

        string json = ProjectFileService.Save(new[] { scene });

        Assert.DoesNotContain("temporary diagnostic", json, StringComparison.Ordinal);
        Assert.DoesNotContain("selectedSceneDiagnostics", json, StringComparison.Ordinal);
    }
}
