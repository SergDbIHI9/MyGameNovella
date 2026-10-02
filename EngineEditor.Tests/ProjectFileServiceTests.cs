using System;
using System.Linq;
using EngineEditor.Models;
using Xunit;

namespace EngineEditor.Tests;

public sealed class ProjectFileServiceTests
{
    [Fact]
    public void LegacySceneArrayMigratesNameReferencesToStableIds()
    {
        const string legacyProject = """
            [
              { "name": "Start", "targetSceneName": "End" },
              { "name": "End" }
            ]
            """;

        var scenes = ProjectFileService.Load(legacyProject);

        Assert.Equal(2, scenes.Count);
        Assert.Equal(scenes[1].Id, scenes[0].TargetSceneId);
        Assert.Equal("End", scenes[0].TargetSceneName);
        Assert.NotEqual(scenes[0].Id, scenes[1].Id);
    }

    [Fact]
    public void VersionedProjectRoundTripsStableChoiceReferences()
    {
        var start = new SceneModel { Name = "Start" };
        var end = new SceneModel { Name = "End" };
        start.Choices.Add(new ChoiceModel
        {
            Text = "Continue",
            TargetSceneName = end.Name,
            TargetSceneId = end.Id
        });

        string json = ProjectFileService.Save(new[] { start, end });
        var loaded = ProjectFileService.Load(json);

        Assert.Contains("\"formatVersion\": 1", json);
        Assert.Equal(end.Id, loaded[0].Choices.Single().TargetSceneId);
    }

    [Fact]
    public void LegacyProjectWithAmbiguousNameReferenceIsRejected()
    {
        const string legacyProject = """
            [
              { "name": "Start", "targetSceneName": "End" },
              { "name": "End" },
              { "name": "End" }
            ]
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectFileService.Load(legacyProject));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedProjectVersionIsRejected()
    {
        const string futureProject = """
            { "formatVersion": 99, "scenes": [{ "name": "Start" }] }
            """;

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProjectFileService.Load(futureProject));

        Assert.Contains("Unsupported project format version", exception.Message);
    }
}
