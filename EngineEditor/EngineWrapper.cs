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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void ChoiceClickedCallback(int choiceIndex);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterChoiceCallback(ChoiceClickedCallback callback);

        // --- НОВОЕ: Коллбэк клика по окну для смены сцен ---
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void WindowClickedCallback();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void RegisterClickCallback(WindowClickedCallback callback);

        // --- НОВОЕ: Настройка размера текста в ядре ---
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

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern bool TickEngine();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void CloseEngine();

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetMusicVolume(float volume);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StartMusicFadeOut(float durationSeconds);
    }
}