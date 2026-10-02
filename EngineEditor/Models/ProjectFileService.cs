using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EngineEditor.Models
{
    public sealed class ProjectDocument
    {
        public int FormatVersion { get; set; } = ProjectFileService.CurrentFormatVersion;
        public List<SceneModel> Scenes { get; set; } = new();
    }

    public static class ProjectFileService
    {
        public const int CurrentFormatVersion = 1;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public static List<SceneModel> Load(string json)
        {
            using var parsed = JsonDocument.Parse(json);
            List<SceneModel>? scenes;
            var isLegacy = parsed.RootElement.ValueKind == JsonValueKind.Array;

            if (isLegacy)
            {
                scenes = JsonSerializer.Deserialize<List<SceneModel>>(json, SerializerOptions);
            }
            else if (parsed.RootElement.ValueKind == JsonValueKind.Object)
            {
                var hasVersion = parsed.RootElement.EnumerateObject()
                    .Any(property => string.Equals(property.Name, "formatVersion", StringComparison.OrdinalIgnoreCase));
                if (!hasVersion)
                    throw new InvalidOperationException("The project document is missing its formatVersion.");

                var document = JsonSerializer.Deserialize<ProjectDocument>(json, SerializerOptions)
                    ?? throw new InvalidOperationException("The project document is empty.");
                if (document.FormatVersion != CurrentFormatVersion)
                {
                    throw new InvalidOperationException(
                        $"Unsupported project format version {document.FormatVersion}. This editor supports version {CurrentFormatVersion}.");
                }

                scenes = document.Scenes;
            }
            else
            {
                throw new InvalidOperationException("A project must be a project document or a legacy scene array.");
            }

            if (scenes is null)
                throw new InvalidOperationException("The project does not contain a scene list.");

            ValidateAndNormalize(scenes, migrateMissingIds: isLegacy);
            return scenes;
        }

        public static string Save(IEnumerable<SceneModel> sourceScenes)
        {
            var scenes = sourceScenes.ToList();
            ValidateAndNormalize(scenes, migrateMissingIds: false);
            return JsonSerializer.Serialize(new ProjectDocument { Scenes = scenes }, SerializerOptions);
        }

        private static void ValidateAndNormalize(IList<SceneModel> scenes, bool migrateMissingIds)
        {
            if (scenes.Count == 0)
                throw new InvalidOperationException("A project must contain at least one scene.");

            for (var index = 0; index < scenes.Count; index++)
            {
                var scene = scenes[index];
                if (scene is null)
                    throw new InvalidOperationException($"Scene {index + 1} is null.");
                if (string.IsNullOrWhiteSpace(scene.Name))
                    throw new InvalidOperationException($"Scene {index + 1} must have a name.");
                if (migrateMissingIds && string.IsNullOrWhiteSpace(scene.Id))
                    scene.Id = Guid.NewGuid().ToString();
                if (string.IsNullOrWhiteSpace(scene.Id))
                    throw new InvalidOperationException($"Scene '{scene.Name}' is missing its stable ID.");
                scene.Choices ??= new System.Collections.ObjectModel.ObservableCollection<ChoiceModel>();
            }

            var duplicateId = scenes.GroupBy(scene => scene.Id, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateId is not null)
                throw new InvalidOperationException($"Duplicate scene ID '{duplicateId.Key}'.");

            var byId = scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
            var byName = scenes.GroupBy(scene => scene.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            foreach (var scene in scenes)
            {
                NormalizeReference(scene.Name, scene.TargetSceneName, scene.TargetSceneId,
                    byName, byId, (id, name) => { scene.TargetSceneId = id; scene.TargetSceneName = name; });
                NormalizeReference(scene.Name, scene.TargetSceneIfTrue, scene.TargetSceneIfTrueId,
                    byName, byId, (id, name) => { scene.TargetSceneIfTrueId = id; scene.TargetSceneIfTrue = name; });
                NormalizeReference(scene.Name, scene.TargetSceneIfFalse, scene.TargetSceneIfFalseId,
                    byName, byId, (id, name) => { scene.TargetSceneIfFalseId = id; scene.TargetSceneIfFalse = name; });

                foreach (var choice in scene.Choices)
                {
                    if (choice is null)
                        throw new InvalidOperationException($"Scene '{scene.Name}' contains a null choice.");
                    NormalizeReference(scene.Name, choice.TargetSceneName, choice.TargetSceneId,
                        byName, byId, (id, name) => { choice.TargetSceneId = id; choice.TargetSceneName = name; });
                }
            }
        }

        private static void NormalizeReference(
            string sourceName,
            string targetName,
            string targetId,
            IReadOnlyDictionary<string, List<SceneModel>> byName,
            IReadOnlyDictionary<string, SceneModel> byId,
            Action<string, string> setReference)
        {
            if (string.IsNullOrWhiteSpace(targetId) && string.IsNullOrWhiteSpace(targetName))
            {
                setReference("", "");
                return;
            }

            if (!string.IsNullOrWhiteSpace(targetId))
            {
                if (!byId.TryGetValue(targetId, out var target))
                    throw new InvalidOperationException($"Scene '{sourceName}' references unknown scene ID '{targetId}'.");
                setReference(target.Id, target.Name);
                return;
            }

            if (!byName.TryGetValue(targetName, out var matches))
                throw new InvalidOperationException($"Scene '{sourceName}' references unknown scene '{targetName}'.");
            if (matches.Count != 1)
                throw new InvalidOperationException(
                    $"Scene '{sourceName}' references the ambiguous scene name '{targetName}'. Use unique scene names to migrate this project.");

            setReference(matches[0].Id, matches[0].Name);
        }
    }
}
