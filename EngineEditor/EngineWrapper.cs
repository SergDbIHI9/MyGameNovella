using System;
using System.Runtime.InteropServices;

namespace EngineEditor
{
    public static class Engine
    {
        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitEngine(
            int width,
            int height,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string title,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string basePath
        );

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateScene(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string bgName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string charName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string charSpriteName,
            float charX,
            float charY
        );
        
        // --- ВИЗУАЛЬНЫЕ ЭФФЕКТЫ ---

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void ShakeScreen(float duration, float intensity = 10f);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void FlashScreen(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string hexColor,
            float duration = 0.5f);
        
        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetCharacterAnimation(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string animType);
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ChoiceClickedCallback(int choiceIndex);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterChoiceCallback(ChoiceClickedCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WindowClickedCallback();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterClickCallback(WindowClickedCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void HotkeyCallback(int hotkey);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterHotkeyCallback(HotkeyCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void UiActionCallback(int action);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterUiActionCallback(UiActionCallback callback);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPlaybackState(
            [MarshalAs(UnmanagedType.I1)] bool sceneSeen,
            [MarshalAs(UnmanagedType.I1)] bool isChoice);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetGameUiState(int state);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void AddDialogueHistory(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string speaker,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetFontSize(int size);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetTextRevealSpeed(float charactersPerSecond);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void PlaySound([MarshalAs(UnmanagedType.LPUTF8Str)] string soundPath);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopSounds();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetLastErrorMessage([Out] byte[] buffer, int capacity);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void UpdateChoices(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? c1,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? c2,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? c3,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? c4
        );

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void PlayMusic([MarshalAs(UnmanagedType.LPUTF8Str)] string trackName);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopMusic();

        // ИСПРАВЛЕНО: Добавлен параметр deltaTime
        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool TickEngine(float deltaTime);

        // ИСПРАВЛЕНО: EntryPoint указывает на реальное имя функции в C++ (ShutdownEngine)
        [DllImport("EngineCore.dll", EntryPoint = "ShutdownEngine", CallingConvention = CallingConvention.Cdecl)]
        public static extern void CloseEngine();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetMusicVolume(float volume);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartMusicFadeOut(float durationSeconds);
    }
}