using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
        private static Engine.ChoiceClickedCallback? _choiceCallbackDelegate;
        private static Engine.WindowClickedCallback? _clickCallbackDelegate;

        private bool _isEngineRunning = false;
        private bool _needsSceneUpdate = false;

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

            InitEngineCallback(); 

            Scenes.Add(new SceneModel { Name = "Сцена 1: Старт", Text = "Текст первого диалога...", Background = "" });

            ScenesListBox.ItemsSource = Scenes;
            ScenesListBox.SelectedIndex = 0;

            SetupDragAndDrop();
        }

        public void InitEngineCallback()
        {
            if (_choiceCallbackDelegate == null)
            {
                _choiceCallbackDelegate = OnChoiceClicked;
                Engine.RegisterChoiceCallback(_choiceCallbackDelegate);
            }

            if (_clickCallbackDelegate == null)
            {
                _clickCallbackDelegate = OnEngineWindowClicked;
                Engine.RegisterClickCallback(_clickCallbackDelegate);
            }
        }

        private void OnEngineWindowClicked()
        {
            // Переключаем на следующую сцену при клике по экрану C++
            Dispatcher.UIThread.Post(() =>
            {
                if (Scenes.Count > 0)
                {
                    int currentIndex = ScenesListBox.SelectedIndex;
                    int nextIndex = currentIndex + 1;

                    if (nextIndex < Scenes.Count)
                    {
                        ScenesListBox.SelectedIndex = nextIndex;
                    }
                }
            });
        }

        private void SetupDragAndDrop()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string bgDir = Path.Combine(baseDir, "Assets", "Backgrounds");
            string charDir = Path.Combine(baseDir, "Assets", "Characters");
            string audioDir = Path.Combine(baseDir, "Assets", "Audio");

            try
            {
                Directory.CreateDirectory(bgDir);
                Directory.CreateDirectory(charDir);
                Directory.CreateDirectory(audioDir);
            }
            catch (Exception ex) { Console.WriteLine($"Ошибка директорий: {ex.Message}"); }

            DropZoneBackground.AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);
            DropZoneBackground.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
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

            DropZoneCharacter.AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);
            DropZoneCharacter.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
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

            DropZoneMusic.AddHandler(DragDrop.DragOverEvent, (s, e) => e.DragEffects = DragDropEffects.Copy);
            DropZoneMusic.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
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
                if (SceneNameInput != null) SceneNameInput.Text = selected.Name;
                if (CharacterNameInput != null) CharacterNameInput.Text = selected.CharacterName;
                if (SceneTextInput != null) SceneTextInput.Text = selected.Text;

                BgStatusText.Text = string.IsNullOrEmpty(selected.Background) ? "Перетащите сюда картинку" : $"Фон: {Path.GetFileName(selected.Background)}";
                CharStatusText.Text = string.IsNullOrEmpty(selected.CharacterSprite) ? "Перетащите сюда спрайт" : $"Спрайт: {Path.GetFileName(selected.CharacterSprite)}";
                MusicStatusText.Text = string.IsNullOrEmpty(selected.Music) ? "Перетащите сюда аудиофайл" : $"Музыка: {Path.GetFileName(selected.Music)}";

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
                if (sender == SceneNameInput && SceneNameInput != null) selected.Name = SceneNameInput.Text ?? "";
                if (sender == CharacterNameInput && CharacterNameInput != null) selected.CharacterName = CharacterNameInput.Text ?? "";
                if (sender == SceneTextInput && SceneTextInput != null) selected.Text = SceneTextInput.Text ?? "";
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

        private void OnFontSizeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int size = (int)e.NewValue;
            if (FontSizeTxt != null) FontSizeTxt.Text = $"{size}px";
            if (_isEngineRunning)
            {
                Engine.SetFontSize(size);
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

            SendSceneToEngine(selected);
        }

        public void OnStartEngineClick(object? sender, RoutedEventArgs e)
        {
            if (_isEngineRunning) return;
            _isEngineRunning = true;

            if (ScenesListBox.SelectedItem is not SceneModel selected) return;

            int initialFontSize = (int)FontSizeSlider.Value;

            Thread engineThread = new Thread(() =>
            {
                try
                {
                    string basePath = AppDomain.CurrentDomain.BaseDirectory;
                    Engine.InitEngine(1024, 576, "Engine Runtime Core", basePath);
                    Engine.SetFontSize(initialFontSize);

                    SendSceneToEngine(selected);
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

        private void OnChoiceClicked(int choiceIndex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var selectedScene = ScenesListBox.SelectedItem as SceneModel;
                if (selectedScene == null || choiceIndex < 0 || choiceIndex >= selectedScene.Choices.Count)
                    return;

                var clickedChoice = selectedScene.Choices[choiceIndex];
                string nextSceneName = clickedChoice.TargetSceneName;

                var nextScene = Scenes.FirstOrDefault(s => s.Name == nextSceneName);

                if (nextScene != null)
                {
                    ScenesListBox.SelectedItem = nextScene;
                    SendSceneToEngine(nextScene);
                }
            });
        }

        public void OnAddChoiceClick(object? sender, RoutedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected && selected.Choices.Count < 4)
            {
                selected.Choices.Add(new ChoiceModel());
                TriggerEngineUpdate();
            }
        }

        public void OnRemoveChoiceClick(object? sender, RoutedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected && sender is Button btn && btn.CommandParameter is ChoiceModel choice)
            {
                selected.Choices.Remove(choice);
                TriggerEngineUpdate();
            }
        }

        public void SendSceneToEngine(SceneModel scene)
        {
            if (scene == null || !_isEngineRunning) return;

            Engine.UpdateScene(scene.Background, scene.Text, scene.CharacterName, scene.CharacterSprite, scene.CharacterX, scene.CharacterY);

            string? c1 = scene.Choices.Count > 0 ? scene.Choices[0].Text : null;
            string? c2 = scene.Choices.Count > 1 ? scene.Choices[1].Text : null;
            string? c3 = scene.Choices.Count > 2 ? scene.Choices[2].Text : null;
            string? c4 = scene.Choices.Count > 3 ? scene.Choices[3].Text : null;

            Engine.UpdateChoices(c1, c2, c3, c4);
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