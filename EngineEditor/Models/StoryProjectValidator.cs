using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace EngineEditor.Models
{
    public enum StoryDiagnosticSeverity
    {
        Warning,
        Error
    }

    public sealed record StoryDiagnostic(StoryDiagnosticSeverity Severity, string Message);

    public static class StoryProjectValidator
    {
        private static readonly HashSet<string> SupportedNodeTypes =
            new(StringComparer.Ordinal) { "Dialogue", "Choice", "Action", "Condition" };

        private static readonly HashSet<string> SupportedConditionOperators =
            new(StringComparer.Ordinal) { "==", "!=", ">", "<", ">=", "<=" };

        private static readonly Regex LuaAssetCallPattern = new(
            @"\b(?:play_music|play_sound)\s*\(\s*(['""])(?<path>[^'""]+)\1",
            RegexOptions.CultureInvariant);

        public static IReadOnlyList<StoryDiagnostic> Validate(
            IEnumerable<SceneModel> sourceScenes,
            string? assetBaseDirectory = null)
        {
            var scenes = sourceScenes.ToArray();
            var diagnostics = new List<StoryDiagnostic>();

            if (scenes.Length == 0)
            {
                diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, "Проект не содержит сцен."));
                return diagnostics;
            }

            foreach (var duplicate in scenes.GroupBy(scene => scene.Id, StringComparer.Ordinal).Where(group => group.Count() > 1))
                diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Повторяется ID сцены «{duplicate.Key}»."));

            foreach (var scene in scenes)
            {
                if (string.IsNullOrWhiteSpace(scene.Name))
                {
                    diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, "У сцены отсутствует имя."));
                    continue;
                }

                if (!SupportedNodeTypes.Contains(scene.NodeType))
                    diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Сцена «{scene.Name}» имеет неизвестный тип «{scene.NodeType}»."));

                ValidateAsset(scene, scene.Background, "фон", assetBaseDirectory, diagnostics);
                ValidateAsset(scene, scene.CharacterSprite, "спрайт персонажа", assetBaseDirectory, diagnostics);
                ValidateAsset(scene, scene.Music, "музыка", assetBaseDirectory, diagnostics);
                ValidateReference(scenes, scene.Name, scene.TargetSceneId, scene.TargetSceneName, "обычный переход", diagnostics);

                if (scene.NodeType == "Choice")
                {
                    if (scene.Choices.Count == 0)
                        diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Узел выбора «{scene.Name}» не содержит вариантов."));
                    if (scene.Choices.Count > 4)
                        diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Узел выбора «{scene.Name}» содержит {scene.Choices.Count} вариантов; runtime поддерживает не более 4."));

                    foreach (var choice in scene.Choices)
                    {
                        if (string.IsNullOrWhiteSpace(choice.Text))
                            diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"В узле «{scene.Name}» есть вариант без текста."));
                        ValidateReference(scenes, scene.Name, choice.TargetSceneId, choice.TargetSceneName, $"вариант «{choice.Text}»", diagnostics);
                    }
                }

                if (scene.NodeType == "Condition")
                {
                    if (!SupportedConditionOperators.Contains(scene.ConditionOperator.Trim()))
                        diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Условие «{scene.Name}» использует неизвестный оператор «{scene.ConditionOperator}»."));
                    if (!float.TryParse(scene.ConditionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                        diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Условие «{scene.Name}» должно сравнивать с конечным числом."));
                    ValidateReference(scenes, scene.Name, scene.TargetSceneIfTrueId, scene.TargetSceneIfTrue, "ветвь true", diagnostics);
                    ValidateReference(scenes, scene.Name, scene.TargetSceneIfFalseId, scene.TargetSceneIfFalse, "ветвь false", diagnostics);
                }

                if (scene.NodeType == "Action" && string.IsNullOrWhiteSpace(scene.TargetSceneId) && string.IsNullOrWhiteSpace(scene.TargetSceneName))
                    diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Warning, $"Действие «{scene.Name}» не имеет следующего перехода."));

                if (scene.NodeType == "Action")
                    ValidateSystemCommand(scene, assetBaseDirectory, diagnostics);

                if (!string.IsNullOrWhiteSpace(scene.LuaScript))
                {
                    try
                    {
                        LuaScriptRunner.ValidateSyntax(scene.LuaScript);
                    }
                    catch (SyntaxErrorException ex)
                    {
                        diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Lua-сценарий «{scene.Name}»: {ex.DecoratedMessage}."));
                    }

                    foreach (Match match in LuaAssetCallPattern.Matches(scene.LuaScript))
                    {
                        string luaAssetPath = match.Groups["path"].Value;
                        if (Path.IsPathRooted(luaAssetPath) || IsProjectTraversalPath(luaAssetPath))
                        {
                            diagnostics.Add(new StoryDiagnostic(
                                StoryDiagnosticSeverity.Error,
                                $"Lua-сценарий «{scene.Name}»: путь к ресурсу должен быть относительным к проекту: «{luaAssetPath}»."));
                        }

                        else
                        {
                            ValidateAsset(scene, luaAssetPath, "ресурс Lua", assetBaseDirectory, diagnostics);
                        }
                    }
                }
            }

            return diagnostics;
        }

        private static bool IsProjectTraversalPath(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            return normalized == ".." || normalized.StartsWith("../", StringComparison.Ordinal)
                || normalized.Contains("/../", StringComparison.Ordinal)
                || normalized.EndsWith("/..", StringComparison.Ordinal);
        }

        private static void ValidateSystemCommand(
            SceneModel scene,
            string? assetBaseDirectory,
            ICollection<StoryDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(scene.SystemCommand))
                return;

            string[] parts = scene.SystemCommand.Split(':', 2);
            string command = parts[0].Trim().ToLowerInvariant();
            string argument = parts.Length > 1 ? parts[1].Trim() : "";

            void AddError(string message) =>
                diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"Действие «{scene.Name}»: {message}"));

            bool IsFiniteNumber(float minValue, float maxValue) =>
                float.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                && float.IsFinite(number)
                && number >= minValue
                && number <= maxValue;

            switch (command)
            {
                case "playmusic":
                case "playsound":
                    if (string.IsNullOrWhiteSpace(argument))
                    {
                        AddError($"команде {command} нужен путь к аудиофайлу.");
                        break;
                    }
                    ValidateAsset(scene, argument, command == "playsound" ? "звуковой эффект" : "музыка",
                        assetBaseDirectory, diagnostics);
                    break;
                case "stopmusic":
                case "stopsound":
                    if (argument.Length > 0)
                        AddError($"команда {command} не принимает аргумент.");
                    break;
                case "volume":
                    if (!IsFiniteNumber(0, 100))
                        AddError("громкость должна быть числом от 0 до 100.");
                    break;
                case "fadeout":
                case "shake":
                    if (!IsFiniteNumber(0, 3600))
                        AddError($"команда {command} ожидает длительность от 0 до 3600 секунд.");
                    break;
                case "fontsize":
                    if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fontSize)
                        || fontSize is < 1 or > 256)
                        AddError("размер шрифта должен быть целым числом от 1 до 256.");
                    break;
                case "setvar":
                    try
                    {
                        StoryRuntimeLogic.ApplyVariableCommand(argument, new Dictionary<string, float>());
                    }
                    catch (FormatException ex)
                    {
                        AddError(ex.Message);
                    }
                    catch (OverflowException ex)
                    {
                        AddError(ex.Message);
                    }
                    break;
                case "flash":
                    string hexColor = argument.StartsWith('#') ? argument[1..] : argument;
                    if (hexColor.Length != 6 || !uint.TryParse(hexColor, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))
                        AddError("цвет вспышки должен иметь формат #RRGGBB.");
                    break;
                default:
                    AddError($"неизвестная команда «{parts[0].Trim()}».");
                    break;
            }
        }

        private static void ValidateAsset(
            SceneModel scene,
            string assetPath,
            string assetType,
            string? assetBaseDirectory,
            ICollection<StoryDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(assetBaseDirectory))
                return;

            try
            {
                string resolvedPath = Path.IsPathRooted(assetPath)
                    ? Path.GetFullPath(assetPath)
                    : Path.GetFullPath(Path.Combine(assetBaseDirectory, assetPath));
                if (!File.Exists(resolvedPath))
                    diagnostics.Add(new StoryDiagnostic(
                        StoryDiagnosticSeverity.Error,
                        $"Сцена «{scene.Name}»: файл {assetType} не найден: «{assetPath}»."));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                diagnostics.Add(new StoryDiagnostic(
                    StoryDiagnosticSeverity.Error,
                    $"Сцена «{scene.Name}»: некорректный путь к ресурсу {assetType}: «{assetPath}»."));
            }
        }

        private static void ValidateReference(
            IReadOnlyCollection<SceneModel> scenes,
            string sourceName,
            string targetId,
            string targetName,
            string linkDescription,
            ICollection<StoryDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(targetId) && string.IsNullOrWhiteSpace(targetName))
            {
                if (linkDescription is "ветвь true" or "ветвь false")
                    diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"У сцены «{sourceName}» не задан переход «{linkDescription}»."));
                else if (linkDescription.StartsWith("вариант", StringComparison.Ordinal))
                    diagnostics.Add(new StoryDiagnostic(StoryDiagnosticSeverity.Error, $"У сцены «{sourceName}» не задан переход для {linkDescription}."));
                return;
            }

            try
            {
                _ = StoryRuntimeLogic.ResolveTarget(scenes, targetId, targetName);
            }
            catch (InvalidOperationException ex)
            {
                diagnostics.Add(new StoryDiagnostic(
                    StoryDiagnosticSeverity.Error,
                    $"Сцена «{sourceName}»: {linkDescription}: {ex.Message}"));
            }
        }
    }
}
