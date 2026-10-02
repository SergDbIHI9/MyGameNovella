using System;
using System.Collections.Generic;
using System.IO;
using EngineEditor.Models;
using MoonSharp.Interpreter;
using Xunit;

namespace EngineEditor.Tests;

public sealed class StoryRuntimeLogicTests
{
    [Fact]
    public void LuaActionsCanUseEngineApiWithoutExposingFilesystemGlobals()
    {
        var variables = new Dictionary<string, float>();
        string? playedTrack = null;
        string? playedSound = null;
        var runner = new LuaScriptRunner(
            variables,
            track => playedTrack = track,
            sound => playedSound = sound,
            () => { },
            () => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { });

        runner.Execute("""
            assert(io == nil and os == nil)
            set_var("HP", get_var("HP") + 5)
            play_music("audio/theme.ogg")
            play_sound("audio/click.wav")
            """);

        Assert.Equal(5, variables["HP"]);
        Assert.Equal("audio/theme.ogg", playedTrack);
        Assert.Equal("audio/click.wav", playedSound);
    }

    [Fact]
    public void LuaRuntimeRejectsInvalidEngineArguments()
    {
        var runner = new LuaScriptRunner(
            new Dictionary<string, float>(),
            _ => { },
            _ => { },
            () => { },
            () => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { },
            _ => { });

        Assert.Throws<ScriptRuntimeException>(() => runner.Execute("set_font_size(1.5)"));
    }

    [Fact]
    public void AssetImportCopiesToProjectAssetsAndAvoidsOverwritingExistingFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"novella-assets-{Guid.NewGuid():N}");
        string project = Path.Combine(root, "project");
        string source = Path.Combine(root, "portrait.png");
        Directory.CreateDirectory(root);
        File.WriteAllText(source, "source");

        try
        {
            string first = ProjectAssetService.Import(source, project);
            string second = ProjectAssetService.Import(source, project);

            Assert.Equal("Assets/portrait.png", first);
            Assert.Equal("Assets/portrait-2.png", second);
            Assert.Equal("source", File.ReadAllText(Path.Combine(project, "Assets", "portrait.png")));
            Assert.Equal("source", File.ReadAllText(Path.Combine(project, "Assets", "portrait-2.png")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GameSaveRoundTripsSceneAndVariablesAndRejectsRemovedScenes()
    {
        string root = Path.Combine(Path.GetTempPath(), $"novella-save-{Guid.NewGuid():N}");
        string saveFile = Path.Combine(root, "slot-1.json");
        var start = new SceneModel { Id = "start", Name = "Start" };
        var scenes = new[] { start };
        var variables = new Dictionary<string, float> { ["HP"] = 12.5f, ["Gold"] = 3 };
        var seenScenes = new[] { "start", "chapter-1" };

        try
        {
            GameSaveService.Save(saveFile, start.Id, variables, seenScenes);
            GameSaveData loaded = GameSaveService.Load(saveFile, scenes);

            Assert.Equal(start.Id, loaded.SceneId);
            Assert.Equal(variables, loaded.Variables);
            Assert.Equal(seenScenes, loaded.SeenSceneIds);
            Assert.NotEqual(default, loaded.SavedAtUtc);
            Assert.Throws<InvalidDataException>(() => GameSaveService.Load(saveFile, Array.Empty<SceneModel>()));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GameSaveSlotsAreProjectScopedAndRejectOutOfRangeIndices()
    {
        string firstProject = Path.Combine(Path.GetTempPath(), "novella", "first.novel");
        string secondProject = Path.Combine(Path.GetTempPath(), "novella", "second.novel");

        Assert.NotEqual(
            GameSaveService.GetSlotSavePath(firstProject, 0),
            GameSaveService.GetSlotSavePath(secondProject, 0));
        Assert.NotEqual(
            GameSaveService.GetSlotSavePath(firstProject, 0),
            GameSaveService.GetSlotSavePath(firstProject, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GameSaveService.GetSlotSavePath(firstProject, GameSaveService.SlotCount));
    }

    [Fact]
    public void ResolveTargetUsesStableIdInsteadOfDuplicateDisplayName()
    {
        var first = new SceneModel { Name = "Ending" };
        var second = new SceneModel { Name = "Ending" };

        var resolved = StoryRuntimeLogic.ResolveTarget(new[] { first, second }, second.Id, "Ending");

        Assert.Same(second, resolved);
    }

    [Fact]
    public void ResolveTargetRejectsAmbiguousNameReferences()
    {
        var scenes = new[] { new SceneModel { Name = "Ending" }, new SceneModel { Name = "Ending" } };

        var exception = Assert.Throws<InvalidOperationException>(
            () => StoryRuntimeLogic.ResolveTarget(scenes, "", "Ending"));

        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VariableCommandsAssignAddAndSubtractInvariantValues()
    {
        var variables = new Dictionary<string, float>();

        StoryRuntimeLogic.ApplyVariableCommand("HP = 10.5", variables);
        StoryRuntimeLogic.ApplyVariableCommand("HP + 2.5", variables);
        StoryRuntimeLogic.ApplyVariableCommand("HP - 1", variables);

        Assert.Equal(12, variables["HP"]);
    }

    [Fact]
    public void MalformedVariableCommandIsReportedAndDoesNotChangeVariables()
    {
        var variables = new Dictionary<string, float> { ["HP"] = 3 };

        Assert.Throws<FormatException>(() => StoryRuntimeLogic.ApplyVariableCommand("HP = invalid", variables));

        Assert.Equal(3, variables["HP"]);
    }

    [Fact]
    public void ConditionsUseInvariantValuesAndRejectInvalidOperators()
    {
        var scene = new SceneModel
        {
            Name = "Check",
            ConditionVariable = "HP",
            ConditionOperator = ">=",
            ConditionValue = "3.5"
        };
        var variables = new Dictionary<string, float> { ["HP"] = 4 };

        Assert.True(StoryRuntimeLogic.EvaluateCondition(scene, variables));

        scene.ConditionOperator = "contains";
        Assert.Throws<FormatException>(() => StoryRuntimeLogic.EvaluateCondition(scene, variables));
    }

    [Fact]
    public void ValidatorReportsMissingChoiceTargetsAndUnsupportedChoiceCount()
    {
        var choiceNode = new SceneModel { Name = "Question", NodeType = "Choice" };
        for (int index = 0; index < 5; index++)
            choiceNode.Choices.Add(new ChoiceModel { Text = $"Option {index}" });

        var errors = StoryProjectValidator.Validate(new[] { choiceNode });

        Assert.Contains(errors, issue => issue.Message.Contains("не более 4", StringComparison.Ordinal));
        Assert.Contains(errors, issue => issue.Message.Contains("не задан переход", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorReportsMissingAssetsAndInvalidLuaBeforePreview()
    {
        var scene = new SceneModel
        {
            Name = "Start",
            Background = "missing-background.png",
            LuaScript = "if then"
        };

        var errors = StoryProjectValidator.Validate(new[] { scene }, AppContext.BaseDirectory);

        Assert.Contains(errors, issue => issue.Message.Contains("не найден", StringComparison.Ordinal));
        Assert.Contains(errors, issue => issue.Message.Contains("Lua-сценарий", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorChecksActionSoundFilesAndRejectsUnknownCommands()
    {
        var missingSound = new SceneModel
        {
            Name = "Missing sound",
            NodeType = "Action",
            SystemCommand = "PlaySound: missing-click.wav"
        };
        var unknownCommand = new SceneModel
        {
            Name = "Unknown action",
            NodeType = "Action",
            SystemCommand = "Teleport: nowhere"
        };

        var errors = StoryProjectValidator.Validate(new[] { missingSound, unknownCommand }, AppContext.BaseDirectory);

        Assert.Contains(errors, issue => issue.Message.Contains("звуковой эффект не найден", StringComparison.Ordinal));
        Assert.Contains(errors, issue => issue.Message.Contains("неизвестная команда", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorChecksStaticLuaMediaPaths()
    {
        var scene = new SceneModel
        {
            Name = "Lua media",
            NodeType = "Action",
            LuaScript = "play_music('missing.ogg')"
        };

        var errors = StoryProjectValidator.Validate(new[] { scene }, AppContext.BaseDirectory);

        Assert.Contains(errors, issue => issue.Message.Contains("ресурс Lua не найден", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsLuaMediaPathsThatEscapeTheProject()
    {
        var scene = new SceneModel
        {
            Name = "Lua traversal",
            NodeType = "Action",
            LuaScript = "play_sound('../outside.wav')"
        };

        var errors = StoryProjectValidator.Validate(new[] { scene }, AppContext.BaseDirectory);

        Assert.Contains(errors, issue => issue.Message.Contains("путь к ресурсу должен быть относительным", StringComparison.Ordinal));
    }
}
