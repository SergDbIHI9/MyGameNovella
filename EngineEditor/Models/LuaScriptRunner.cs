using System;
using System.Collections.Generic;
using MoonSharp.Interpreter;

namespace EngineEditor.Models
{
    public sealed class LuaScriptRunner
    {
        private readonly IDictionary<string, float> _variables;
        private readonly Action<string> _playMusic;
        private readonly Action<string> _playSound;
        private readonly Action _stopMusic;
        private readonly Action _stopSounds;
        private readonly Action<float> _setVolume;
        private readonly Action<float> _fadeMusic;
        private readonly Action<int> _setFontSize;
        private readonly Action<float> _shake;
        private readonly Action<string> _flash;

        public LuaScriptRunner(
            IDictionary<string, float> variables,
            Action<string> playMusic,
            Action<string> playSound,
            Action stopMusic,
            Action stopSounds,
            Action<float> setVolume,
            Action<float> fadeMusic,
            Action<int> setFontSize,
            Action<float> shake,
            Action<string> flash)
        {
            _variables = variables;
            _playMusic = playMusic;
            _playSound = playSound;
            _stopMusic = stopMusic;
            _stopSounds = stopSounds;
            _setVolume = setVolume;
            _fadeMusic = fadeMusic;
            _setFontSize = setFontSize;
            _shake = shake;
            _flash = flash;
        }

        public void Execute(string source)
        {
            var script = new Script(CoreModules.Preset_HardSandbox);
            script.Globals["get_var"] = (Func<string, double>)GetVariable;
            script.Globals["set_var"] = (Action<string, double>)SetVariable;
            script.Globals["play_music"] = _playMusic;
            script.Globals["play_sound"] = _playSound;
            script.Globals["stop_music"] = _stopMusic;
            script.Globals["stop_sound"] = _stopSounds;
            script.Globals["set_volume"] = (Action<double>)(value => _setVolume(ToFiniteFloat(value, "volume")));
            script.Globals["fade_music"] = (Action<double>)(seconds => _fadeMusic(ToFiniteFloat(seconds, "fade duration")));
            script.Globals["set_font_size"] = (Action<double>)(size => _setFontSize(ToPositiveInt(size, "font size")));
            script.Globals["shake"] = (Action<double>)(seconds => _shake(ToFiniteFloat(seconds, "shake duration")));
            script.Globals["flash"] = _flash;
            script.DoString(source);
        }

        public static void ValidateSyntax(string source)
        {
            var script = new Script(CoreModules.Preset_HardSandbox);
            _ = script.LoadString(source);
        }

        private double GetVariable(string name) =>
            _variables.TryGetValue(name, out float value) ? value : 0d;

        private void SetVariable(string name, double value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ScriptRuntimeException("Variable name cannot be empty.");

            _variables[name] = ToFiniteFloat(value, "variable value");
        }

        private static float ToFiniteFloat(double value, string parameterName)
        {
            if (!double.IsFinite(value) || value > float.MaxValue || value < -float.MaxValue)
                throw new ScriptRuntimeException($"{parameterName} must be a finite single-precision number.");

            return (float)value;
        }

        private static int ToPositiveInt(double value, string parameterName)
        {
            if (!double.IsFinite(value) || value < 1 || value > int.MaxValue || Math.Truncate(value) != value)
                throw new ScriptRuntimeException($"{parameterName} must be a positive integer.");

            return (int)value;
        }
    }
}
