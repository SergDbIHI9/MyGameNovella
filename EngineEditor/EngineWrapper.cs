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
        
        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void UpdateScene(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string bgName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string charName,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string charSpriteName,
            float charX,
            float charY
        );

        // --- НОВЫЕ ФУНКЦИИ ---
        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void PlayMusic([MarshalAs(UnmanagedType.LPUTF8Str)] string trackName);

        [DllImport("EngineCore.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void StopMusic();
        // ---------------------

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