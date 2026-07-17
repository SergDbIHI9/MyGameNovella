using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using EngineEditor.Models;
using Avalonia.Platform.Storage;


namespace EngineEditor.Views
{
    public partial class MainWindow : Window
    {
        private bool _isEngineRunning = false;
        private bool _needsSceneUpdate = false;
        private string _pendingBg = "";
        private string _pendingText = "";
        private string _pendingMusic = "";
        private bool _musicChanged = false;
        private readonly object _syncLock = new object();

        // Наш динамический список сцен
        public ObservableCollection<SceneModel> Scenes { get; set; } = new ObservableCollection<SceneModel>();

        public MainWindow()
        {
            InitializeComponent();

            // Заполняем дефолтными сценами для старта
            Scenes.Add(new SceneModel { Name = "Сцена 1: Начало", Text = "Давным-давно в далекой галактике...", Background = "bg1.jpg" });
            Scenes.Add(new SceneModel { Name = "Сцена 2: Встреча", Text = "Вдруг из темноты появился таинственный силуэт.", Background = "bg2.jpg" });

            ScenesListBox.ItemsSource = Scenes;
            ScenesListBox.SelectedIndex = 0;

            // Настраиваем события Drag and Drop
            SetupDragAndDrop();
        }

        private void SetupDragAndDrop()
        {
            // Настройка для ФОНА
            DropZoneBackground.AddHandler(DragDrop.DragOverEvent, (s, e) =>
            {
                e.DragEffects = DragDropEffects.Copy;
            });
            DropZoneBackground.AddHandler(DragDrop.DropEvent, (s, e) =>
            {
                var files = e.DataTransfer.TryGetFiles();
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        string filePath = file.Path.LocalPath;
                        string fileName = Path.GetFileName(filePath);

                        if (ScenesListBox.SelectedItem is SceneModel currentScene)
                        {
                            currentScene.Background = fileName;
                            BgStatusText.Text = $"Фон установлен: {fileName}";
                            TriggerEngineUpdate();
                        }
                    }
                }
                ;

                // Настройка для МУЗЫКИ
                DropZoneMusic.AddHandler(DragDrop.DragOverEvent, (s, e) =>
                {
                    e.DragEffects = DragDropEffects.Copy;
                });
                DropZoneMusic.AddHandler(DragDrop.DropEvent, (s, e) =>
                {
                    var files = e.DataTransfer.TryGetFiles();
                    if (files != null)
                    {
                        foreach (var file in files)
                        {
                            string filePath = file.Path.LocalPath;
                            string fileName = Path.GetFileName(filePath);

                            if (ScenesListBox.SelectedItem is SceneModel currentScene)
                            {
                                currentScene.Music = fileName;
                                MusicStatusText.Text = $"Музыка установлена: {fileName}";
                                TriggerEngineUpdate(musicChanged: true);
                            }
                        }
                    }
                });
            });
            }

        // --- УПРАВЛЕНИЕ СПИСКОМ ---

        private void OnAddSceneClick(object? sender, RoutedEventArgs e)
        {
            var newScene = new SceneModel
            {
                Name = $"Сцена {Scenes.Count + 1}",
                Text = "Новый диалог...",
                Background = "bg1.jpg"
            };
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

        // Выбор другой сцены в списке
        private void OnSceneSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                // Заполняем поля ввода данными выбранной сцены
                SceneNameInput.Text = selected.Name;
                SceneTextInput.Text = selected.Text;
                BgStatusText.Text = $"Фон установлен: {selected.Background}";
                MusicStatusText.Text = string.IsNullOrEmpty(selected.Music) ? "Перетащите сюда аудиофайл (.mp3/.ogg)" : $"Музыка установлена: {selected.Music}";

                // Если движок работает, сразу обновляем его!
                TriggerEngineUpdate(musicChanged: true);
            }
        }

        // Событие ручного редактирования полей текста или имени
        private void OnSceneDataChanged(object? sender, TextChangedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                if (sender == SceneNameInput) selected.Name = SceneNameInput.Text ?? "";
                if (sender == SceneTextInput) selected.Text = SceneTextInput.Text ?? "";

                TriggerEngineUpdate();
            }
        }

        // Отправка данных в C++ движок
        private void TriggerEngineUpdate(bool musicChanged = false)
        {
            if (!_isEngineRunning || ScenesListBox.SelectedItem is not SceneModel selected) return;

            lock (_syncLock)
            {
                _pendingBg = selected.Background;
                _pendingText = selected.Text;

                if (musicChanged)
                {
                    _pendingMusic = selected.Music;
                    _musicChanged = true;
                }

                _needsSceneUpdate = true;
            }
        }

        // --- ДВИЖОК ---

        public void OnStartEngineClick(object? sender, RoutedEventArgs e)
        {
            if (_isEngineRunning) return;
            _isEngineRunning = true;

            string bg = "bg1.jpg";
            string text = "Добро пожаловать!";
            string music = "";

            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                bg = selected.Background;
                text = selected.Text;
                music = selected.Music;
            }

            Thread engineThread = new Thread(() =>
            {
                try
                {
                    string basePath = AppDomain.CurrentDomain.BaseDirectory;
                    Engine.InitEngine(1024, 576, "Engine Runtime (PowerPoint Mode)", basePath);

                    Engine.UpdateScene(bg, text);
                    if (!string.IsNullOrEmpty(music)) Engine.PlayMusic(music);

                    while (_isEngineRunning)
                    {
                        lock (_syncLock)
                        {
                            if (_needsSceneUpdate)
                            {
                                Engine.UpdateScene(_pendingBg, _pendingText);

                                if (_musicChanged)
                                {
                                    if (string.IsNullOrEmpty(_pendingMusic))
                                        Engine.StopMusic();
                                    else
                                        Engine.PlayMusic(_pendingMusic);

                                    _musicChanged = false;
                                }

                                _needsSceneUpdate = false;
                            }
                        }
                        if (!Engine.TickEngine()) break;
                        Thread.Sleep(16);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
                finally
                {
                    Engine.CloseEngine();
                    _isEngineRunning = false;
                }
            });

            engineThread.IsBackground = true;
            engineThread.SetApartmentState(ApartmentState.STA);
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