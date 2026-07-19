using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using EngineEditor.Models;

namespace EngineEditor.Views
{
    public partial class MainWindow : Window
    {
        private bool _isEngineRunning = false;
        private bool _needsSceneUpdate = false;
        
        // Синхронизационные кэш-переменные данных
        private string _pendingBg = "";
        private string _pendingText = "";
        private string _pendingCharName = "";
        private string _pendingCharSprite = "";
        private float _pendingCharX = 50f;
        private float _pendingCharY = 100f;
        private string _pendingMusic = "";
        private bool _musicChanged = false;
        
        private readonly object _syncLock = new object();

        public ObservableCollection<SceneModel> Scenes { get; set; } = new ObservableCollection<SceneModel>();

        public MainWindow()
        {
            InitializeComponent();
            
            // Стартовая дефолтная инициализация данных
            Scenes.Add(new SceneModel { Name = "Сцена 1: Старт", Text = "Текст первого диалога новой истории...", Background = "" });
            
            ScenesListBox.ItemsSource = Scenes;
            ScenesListBox.SelectedIndex = 0;

            SetupDragAndDrop();
        }

        private void SetupDragAndDrop()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string bgDir = Path.Combine(baseDir, "Assets", "Backgrounds");
            string charDir = Path.Combine(baseDir, "Assets", "Characters");
            string audioDir = Path.Combine(baseDir, "Assets", "Audio");

            try
            {
                // Изолируем ресурсы проекта по подпапкам структуры Assets
                Directory.CreateDirectory(bgDir);
                Directory.CreateDirectory(charDir);
                Directory.CreateDirectory(audioDir);
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка директорий: {ex.Message}"); }

            // ОБРАБОТКА ИМПОРТА ФОНА
            DropZoneBackground.AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);
            DropZoneBackground.AddHandler(DragDrop.DropEvent, (s, e) => {
                var files = e.DataTransfer.TryGetFiles();
                if (files != null && files.Any())
                {
                    string sourcePath = files.First().Path.LocalPath;
                    string fileName = Path.GetFileName(sourcePath);
                    string relativePath = Path.Combine("Assets", "Backgrounds", fileName).Replace('\\', '/');
                    string destinationPath = Path.Combine(baseDir, relativePath);

                    try
                    {
                        if (Path.GetFullPath(sourcePath) != Path.GetFullPath(destinationPath))
                            File.Copy(sourcePath, destinationPath, true);

                        if (ScenesListBox.SelectedItem is SceneModel currentScene)
                        {
                            currentScene.Background = relativePath;
                            BgStatusText.Text = $"Импорт графики: {fileName}";
                            TriggerEngineUpdate();
                        }
                    }
                    catch (Exception ex) { BgStatusText.Text = $"Ошибка: {ex.Message}"; }
                }
            });

            // ОБРАБОТКА ИМПОРТА ПЕРСОНАЖА
            DropZoneCharacter.AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);
            DropZoneCharacter.AddHandler(DragDrop.DropEvent, (s, e) => {
                var files = e.DataTransfer.TryGetFiles();
                if (files != null && files.Any())
                {
                    string sourcePath = files.First().Path.LocalPath;
                    string fileName = Path.GetFileName(sourcePath);
                    string relativePath = Path.Combine("Assets", "Characters", fileName).Replace('\\', '/');
                    string destinationPath = Path.Combine(baseDir, relativePath);

                    try
                    {
                        if (Path.GetFullPath(sourcePath) != Path.GetFullPath(destinationPath))
                            File.Copy(sourcePath, destinationPath, true);

                        if (ScenesListBox.SelectedItem is SceneModel currentScene)
                        {
                            currentScene.CharacterSprite = relativePath;
                            CharStatusText.Text = $"Импорт спрайта: {fileName}";
                            TriggerEngineUpdate();
                        }
                    }
                    catch (Exception ex) { CharStatusText.Text = $"Ошибка: {ex.Message}"; }
                }
            });

            // ОБРАБОТКА ИМПОРТА МУЗЫКИ
            DropZoneMusic.AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);
            DropZoneMusic.AddHandler(DragDrop.DropEvent, (s, e) => {
                var files = e.DataTransfer.TryGetFiles();
                if (files != null && files.Any())
                {
                    string sourcePath = files.First().Path.LocalPath;
                    string fileName = Path.GetFileName(sourcePath);
                    string relativePath = Path.Combine("Assets", "Audio", fileName).Replace('\\', '/');
                    string destinationPath = Path.Combine(baseDir, relativePath);

                    try
                    {
                        if (Path.GetFullPath(sourcePath) != Path.GetFullPath(destinationPath))
                            File.Copy(sourcePath, destinationPath, true);

                        if (ScenesListBox.SelectedItem is SceneModel currentScene)
                        {
                            currentScene.Music = relativePath;
                            MusicStatusText.Text = $"Импорт аудио: {fileName}";
                            TriggerEngineUpdate(musicChanged: true);
                        }
                    }
                    catch (Exception ex) { MusicStatusText.Text = $"Ошибка: {ex.Message}"; }
                }
            });
        }

        // --- СОХРАНЕНИЕ / ЗАГРУЗКА ИЗ JSON ПРОЕКТА ---

        private void OnSaveProjectClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(Scenes, options);
                File.WriteAllText("story.novel", jsonString);
                BgStatusText.Text = "Проект сохранен в файл story.novel!";
            }
            catch (Exception ex) { BgStatusText.Text = $"Ошибка записи: {ex.Message}"; }
        }

        private void OnLoadProjectClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists("story.novel"))
                {
                    string jsonString = File.ReadAllText("story.novel");
                    var loadedScenes = JsonSerializer.Deserialize<ObservableCollection<SceneModel>>(jsonString);
                    if (loadedScenes != null)
                    {
                        Scenes.Clear();
                        foreach (var s in loadedScenes) Scenes.Add(s);
                        ScenesListBox.SelectedIndex = 0;
                        BgStatusText.Text = "Проект story.novel успешно загружен!";
                    }
                }
                else { BgStatusText.Text = "Файл story.novel не обнаружен!"; }
            }
            catch (Exception ex) { BgStatusText.Text = $"Ошибка парсинга JSON: {ex.Message}"; }
        }

        // --- УПРАВЛЕНИЕ СПИСКАМИ И СЛАЙДАМИ ---

        private void OnAddSceneClick(object? sender, RoutedEventArgs e)
        {
            var newScene = new SceneModel { Name = $"Сцена {Scenes.Count + 1}", Text = "Новый диалог...", Background = "" };
            Scenes.Add(newScene);
            ScenesListBox.SelectedItem = newScene;
        }

        private void OnDeleteSceneClick(object? sender, RoutedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected && Scenes.Count > 1)
            {
                int index = Scenes.IndexOf(selected);
                Scenes.Remove(selected);
                ScenesListBox.SelectedIndex = Math.Max(0, index - 1);
            }
        }

        private void OnSceneSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                SceneNameInput.Text = selected.Name;
                CharacterNameInput.Text = selected.CharacterName;
                SceneTextInput.Text = selected.Text;
                
                BgStatusText.Text = string.IsNullOrEmpty(selected.Background) ? "Перетащите сюда картинку" : $"Фон: {Path.GetFileName(selected.Background)}";
                CharStatusText.Text = string.IsNullOrEmpty(selected.CharacterSprite) ? "Перетащите сюда спрайт" : $"Спрайт: {Path.GetFileName(selected.CharacterSprite)}";
                MusicStatusText.Text = string.IsNullOrEmpty(selected.Music) ? "Перетащите сюда аудиофайл" : $"Музыка: {Path.GetFileName(selected.Music)}";

                // Обновление UI положения ползунков слайдеров
                CharXSlider.Value = selected.CharacterX;
                CharYSlider.Value = selected.CharacterY;
                if (CharXTxt != null) CharXTxt.Text = $"{(int)selected.CharacterX}%";
                if (CharYTxt != null) CharYTxt.Text = $"{(int)selected.CharacterY}%";

                TriggerEngineUpdate(musicChanged: true);
            }
        }

        private void OnSceneDataChanged(object? sender, TextChangedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                if (sender == SceneNameInput) selected.Name = SceneNameInput.Text ?? "";
                if (sender == CharacterNameInput) selected.CharacterName = CharacterNameInput.Text ?? "";
                if (sender == SceneTextInput) selected.Text = SceneTextInput.Text ?? "";
                TriggerEngineUpdate();
            }
        }

        private void OnCharPositionChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                if (sender == CharXSlider)
                {
                    selected.CharacterX = (float)e.NewValue;
                    if (CharXTxt != null) CharXTxt.Text = $"{(int)e.NewValue}%";
                }
                if (sender == CharYSlider)
                {
                    selected.CharacterY = (float)e.NewValue;
                    if (CharYTxt != null) CharYTxt.Text = $"{(int)e.NewValue}%";
                }
                TriggerEngineUpdate();
            }
        }

        private void TriggerEngineUpdate(bool musicChanged = false)
        {
            if (!_isEngineRunning || ScenesListBox.SelectedItem is not SceneModel selected) return;

            lock (_syncLock)
            {
                _pendingBg = selected.Background;
                _pendingText = selected.Text;
                _pendingCharName = selected.CharacterName;
                _pendingCharSprite = selected.CharacterSprite;
                _pendingCharX = selected.CharacterX;
                _pendingCharY = selected.CharacterY;

                if (musicChanged)
                {
                    _pendingMusic = selected.Music;
                    _musicChanged = true;
                }
                _needsSceneUpdate = true;
            }
        }

        // --- ПОТОК ВЗАИМОДЕЙСТВИЯ С РЕНДЕРИНГОМ NATIVE CORE ---

        public void OnStartEngineClick(object? sender, RoutedEventArgs e)
        {
            if (_isEngineRunning) return;
            _isEngineRunning = true;

            if (ScenesListBox.SelectedItem is not SceneModel selected) return;

            Thread engineThread = new Thread(() =>
            {
                try
                {
                    string basePath = AppDomain.CurrentDomain.BaseDirectory;
                    Engine.InitEngine(1024, 576, "Engine Runtime Core", basePath);
                    
                    Engine.UpdateScene(selected.Background, selected.Text, selected.CharacterName, selected.CharacterSprite, selected.CharacterX, selected.CharacterY);
                    if (!string.IsNullOrEmpty(selected.Music)) Engine.PlayMusic(selected.Music);

                    while (_isEngineRunning)
                    {
                        lock (_syncLock)
                        {
                            if (_needsSceneUpdate)
                            {
                                Engine.UpdateScene(_pendingBg, _pendingText, _pendingCharName, _pendingCharSprite, _pendingCharX, _pendingCharY);
                                if (_musicChanged)
                                {
                                    if (string.IsNullOrEmpty(_pendingMusic)) Engine.StopMusic();
                                    else Engine.PlayMusic(_pendingMusic);
                                    _musicChanged = false;
                                }
                                _needsSceneUpdate = false;
                            }
                        }
                        if (!Engine.TickEngine()) break;
                        Thread.Sleep(16);
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Ошибка Runtime: {ex.Message}"); }
                finally
                {
                    Engine.CloseEngine();
                    _isEngineRunning = false;
                }
            });

            engineThread.IsBackground = true;
            if (OperatingSystem.IsWindows()) engineThread.SetApartmentState(ApartmentState.STA);
            engineThread.Start();
        }

        public void OnStopEngineClick(object? sender, RoutedEventArgs e) => _isEngineRunning = false;

        public void OnVolumeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            float volume = (float)e.NewValue;
            if (VolumeTxt != null) VolumeTxt.Text = $"{(int)volume}%";
            if (_isEngineRunning) Engine.SetMusicVolume(volume);
        }

        public void OnFadeOutClick(object? sender, RoutedEventArgs e)
        {
            if (_isEngineRunning) Engine.StartMusicFadeOut(2.0f);
        }

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _isEngineRunning = false;
            base.OnClosing(e);
        }
    }
}