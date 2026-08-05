using System;
using System.Runtime.InteropServices;

namespace EngineEditor
{
    public static class Engine
    {
        // ИСПРАВЛЕНО: Убран 4-й параметр basePath, чтобы соответствовать C++ API
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

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void FlashScreen(string hexColor, float duration = 0.5f);
        
        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void SetCharacterAnimation(string animType);
        
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ChoiceClickedCallback(int choiceIndex);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterChoiceCallback(ChoiceClickedCallback callback);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WindowClickedCallback();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterClickCallback(WindowClickedCallback callback);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetFontSize(int size);

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