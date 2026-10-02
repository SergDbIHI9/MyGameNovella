using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace EngineEditor.Models
{
    public static partial class StoryRuntimeLogic
    {
        private static readonly Regex VariableAssignmentPattern = new(
            @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?<operator>=|\+|-)\s*(?<value>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*$",
            RegexOptions.CultureInvariant);

        private static readonly Regex TextVariablePattern = new(
            @"\{([A-Za-z_][A-Za-z0-9_]*)\}",
            RegexOptions.CultureInvariant);

        public static SceneModel? ResolveTarget(
            IEnumerable<SceneModel> scenes,
            string? targetId,
            string? targetName)
        {
            var sceneList = scenes as IReadOnlyCollection<SceneModel> ?? scenes.ToArray();

            if (!string.IsNullOrWhiteSpace(targetId))
            {
                var target = sceneList.SingleOrDefault(scene => scene.Id == targetId);
                return target ?? throw new InvalidOperationException($"Unknown scene ID '{targetId}'.");
            }

            if (string.IsNullOrWhiteSpace(targetName))
                return null;

            var matches = sceneList.Where(scene => scene.Name == targetName).Take(2).ToArray();
            return matches.Length switch
            {
                0 => throw new InvalidOperationException($"Unknown scene '{targetName}'."),
                1 => matches[0],
                _ => throw new InvalidOperationException($"Scene name '{targetName}' is ambiguous.")
            };
        }

        public static bool EvaluateCondition(
            SceneModel scene,
            IReadOnlyDictionary<string, float> variables)
        {
            if (!float.TryParse(scene.ConditionValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float targetValue)
                || !float.IsFinite(targetValue))
            {
                throw new FormatException($"Condition '{scene.Name}' has an invalid numeric value.");
            }

            float currentValue = variables.TryGetValue(scene.ConditionVariable.Trim(), out float value) ? value : 0;
            return scene.ConditionOperator.Trim() switch
            {
                "==" => currentValue == targetValue,
                ">" => currentValue > targetValue,
                "<" => currentValue < targetValue,
                ">=" => currentValue >= targetValue,
                "<=" => currentValue <= targetValue,
                "!=" => currentValue != targetValue,
                var op => throw new FormatException($"Condition '{scene.Name}' has unsupported operator '{op}'.")
            };
        }

        public static void ApplyVariableCommand(string expression, IDictionary<string, float> variables)
        {
            var match = VariableAssignmentPattern.Match(expression);
            if (!match.Success)
                throw new FormatException("Expected a command such as 'HP = 10', 'HP + 5' or 'HP - 2'.");

            string name = match.Groups["name"].Value;
            string operation = match.Groups["operator"].Value;
            if (!float.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float operand)
                || !float.IsFinite(operand))
            {
                throw new FormatException("The variable command value must be a finite number.");
            }

            float currentValue = variables.TryGetValue(name, out float value) ? value : 0;
            float newValue = operation switch
            {
                "=" => operand,
                "+" => currentValue + operand,
                "-" => currentValue - operand,
                _ => throw new InvalidOperationException($"Unsupported variable operation '{operation}'.")
            };
            if (!float.IsFinite(newValue))
                throw new OverflowException($"Variable '{name}' would exceed the supported numeric range.");

            variables[name] = newValue;
        }

        public static string SubstituteTextVariables(
            string? text,
            IReadOnlyDictionary<string, float> variables)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? "";

            return TextVariablePattern.Replace(text, match =>
                variables.TryGetValue(match.Groups[1].Value, out float value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : match.Value);
        }
    }
}
