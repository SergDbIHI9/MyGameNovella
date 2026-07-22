using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using System.Text.Json;
using System.Threading;
using EngineEditor.Models;

namespace EngineEditor.Views
{
    public partial class MainWindow : Window
    {
        // --- ДЕЛЕГАТЫ ДВИЖКА ---
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

        // --- ПЕРЕМЕННЫЕ НОДОВОЙ СИСТЕМЫ ---
        // Поля для зума и перемещения канваса
        private readonly ScaleTransform _canvasScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _canvasPan = new TranslateTransform(0, 0);

        private double _zoom = 1.0;
        private bool _isPanningCanvas = false;
        private Point _lastPanPoint;
        
        private bool _isDraggingNode = false;
        private Point _dragOffset;
        private SceneModel? _draggedScene = null;
        private Control? _draggedContainer = null; // визуальный контейнер (ContentPresenter) перетаскиваемого нода

        // состояние протяжки связи между нодами (от коннектора к другому ноду)
        private bool _isLinkingNodes = false;
        private SceneModel? _linkSourceScene = null;

        // Переменные для D&D из палитры
        private bool _isDraggingFromPalette = false;
        private string? _pendingNodeType = null;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_canvasScale);
            transformGroup.Children.Add(_canvasPan);
            GraphCanvas.RenderTransform = transformGroup;
            
            InitEngineCallback();

            Scenes.Add(new SceneModel { Name = "Сцена 1: Старт", Text = "Текст первого диалога...", X = 50, Y = 100, HeaderColor = "#007acc" });
            Scenes.Add(new SceneModel { Name = "Сцена 2: Выбор", Text = "Куда пойдем?", X = 400, Y = 250, HeaderColor = "#ffc107" });
            Scenes[0].Choices.Add(new ChoiceModel { Text = "Далее", TargetSceneName = Scenes[1].Name });

            ScenesListBox.ItemsSource = Scenes;
            ScenesListBox.SelectedIndex = 0;

            UpdateConnectionLine();
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

        // ==========================================
        //  БЕСШОВНАЯ СИСТЕМА D&D (УРОВЕНЬ ОКНА)
        // ==========================================
        private void OnCanvasPointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is Border border)
            {
                var pointerPos = e.GetPosition(border);

                double zoomDelta = e.Delta.Y > 0 ? 1.1 : 0.9;
                double newZoom = Math.Clamp(_zoom * zoomDelta, 0.2, 3.0);
                double actualZoomDelta = newZoom / _zoom;
                _zoom = newZoom;

                _canvasScale.ScaleX = _zoom;
                _canvasScale.ScaleY = _zoom;

                // Корректируем смещение относительно позиции курсора
                _canvasPan.X = pointerPos.X - (pointerPos.X - _canvasPan.X) * actualZoomDelta;
                _canvasPan.Y = pointerPos.Y - (pointerPos.Y - _canvasPan.Y) * actualZoomDelta;
            }
        }
        
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                _isPanningCanvas = true;
                _lastPanPoint = e.GetPosition(this); // Запоминаем глобальную позицию мыши
                Cursor = new Cursor(StandardCursorType.SizeAll);
                e.Handled = true;
            }
        }
        
        private void OnPaletteItemPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.Tag is string nodeType)
            {
                _isDraggingFromPalette = true;
                _pendingNodeType = nodeType;

                // Захватываем указатель на самой кнопке палитры
                e.Pointer.Capture(border);
                e.Handled = true;
            }
        }

        private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is SceneModel scene)
            {
                _isDraggingNode = true;
                _draggedScene = scene;
                _draggedContainer = NodesItemsControl.ContainerFromItem(scene);

                var pos = e.GetPosition(GraphCanvas);
                _dragOffset = new Point(pos.X - scene.X, pos.Y - scene.Y);

                // Захватываем указатель на самом ноде
                e.Pointer.Capture(border);
                e.Handled = true;
            }
        }

        // начало протяжки связи от коннектора нода
        private void OnConnectorPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is Control ctrl && ctrl.DataContext is SceneModel scene)
            {
                _isLinkingNodes = true;
                _linkSourceScene = scene;

                e.Pointer.Capture(ctrl);
                e.Handled = true;
            }
        }

        // ГЛОБАЛЬНЫЙ ПЕРЕХВАТ ДВИЖЕНИЯ МЫШИ ДЛЯ ВСЕГО ОКНА
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_isPanningCanvas)
            {
                var currentPos = e.GetPosition(this);
                _canvasPan.X += currentPos.X - _lastPanPoint.X;
                _canvasPan.Y += currentPos.Y - _lastPanPoint.Y;
                _lastPanPoint = currentPos;
                return;
            }
            
            // 1. Если только потащили мышь из палитры — мгновенно создаем нод
            if (_isDraggingFromPalette && _pendingNodeType != null)
            {
                var pos = e.GetPosition(GraphCanvas);

                var newScene = new SceneModel { X = pos.X - 120, Y = pos.Y - 20 };
                ConfigureNewNode(newScene, _pendingNodeType);

                Scenes.Add(newScene);
                ScenesListBox.SelectedItem = newScene;

                // Плавно переключаемся в режим "перетаскивания существующего нода"
                _draggedScene = newScene;
                _isDraggingNode = true;
                _dragOffset = new Point(120, 20);

                _isDraggingFromPalette = false;
                _pendingNodeType = null;
            }

            // 2. Логика физики перемещения
            if (_isDraggingNode && _draggedScene != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                _draggedScene.X = pos.X - _dragOffset.X;
                _draggedScene.Y = pos.Y - _dragOffset.Y;

                // двигаем визуальный контейнер нода напрямую, без биндинга
                _draggedContainer ??= NodesItemsControl.ContainerFromItem(_draggedScene);
                if (_draggedContainer != null)
                {
                    Canvas.SetLeft(_draggedContainer, _draggedScene.X);
                    Canvas.SetTop(_draggedContainer, _draggedScene.Y);
                }

                UpdateConnectionLine();
            }

            // 3. протяжка временной линии связи между нодами
            if (_isLinkingNodes && _linkSourceScene != null && TempLinkLine != null)
            {
                var pos = e.GetPosition(GraphCanvas);

                double outX = _linkSourceScene.X + 240;
                double outY = _linkSourceScene.Y + 80;

                var geometry = new Avalonia.Media.PathGeometry();
                var figure = new Avalonia.Media.PathFigure { StartPoint = new Point(outX, outY), IsClosed = false };
                figure.Segments.Add(new Avalonia.Media.LineSegment { Point = pos });
                geometry.Figures.Add(figure);
                TempLinkLine.Data = geometry;
            }
        }

        // ГЛОБАЛЬНЫЙ ОТПУСК МЫШИ
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            // Остановка перемещения холста
            if (_isPanningCanvas)
            {
                _isPanningCanvas = false;
                Cursor = new Cursor(StandardCursorType.Arrow);
                e.Handled = true;
                return;
            }
            
            // Если пользователь просто "кликнул" по палитре, а не потащил
            if (_isDraggingFromPalette && _pendingNodeType != null)
            {
                var newScene = new SceneModel { X = 300, Y = 200 }; // Спавним в центре
                ConfigureNewNode(newScene, _pendingNodeType);

                Scenes.Add(newScene);
                ScenesListBox.SelectedItem = newScene;

                _isDraggingFromPalette = false;
                _pendingNodeType = null;
            }

            // Сброс состояния перетаскивания нодов
            if (_isDraggingNode)
            {
                _isDraggingNode = false;
                _draggedScene = null;
                _draggedContainer = null;
            }

            // завершение протяжки связи — ищем нод под курсором и создаём Choice
            if (_isLinkingNodes && _linkSourceScene != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                var target = FindNodeAt(pos, exclude: _linkSourceScene);

                if (target != null)
                {
                    if (_linkSourceScene.NodeType == "Choice")
                    {
                        // Если тянем от выбора — создаем кнопку
                        _linkSourceScene.Choices.Add(new ChoiceModel { Text = "Вариант", TargetSceneName = target.Name });
                    }
                    else
                    {
                        // Если тянем от Диалога или Действия — делаем прямой бесшовный переход
                        _linkSourceScene.TargetSceneName = target.Name;
                    }
                    UpdateConnectionLine();
                }

                _isLinkingNodes = false;
                _linkSourceScene = null;
                if (TempLinkLine != null) TempLinkLine.Data = null;
            }

            e.Pointer.Capture(null);
        }

        // ищет нод, под которым сейчас находится точка (для завершения протяжки связи)
        private SceneModel? FindNodeAt(Point pos, SceneModel? exclude = null)
        {
            foreach (var scene in Scenes)
            {
                if (scene == exclude) continue;

                var container = NodesItemsControl.ContainerFromItem(scene);
                double width = container?.Bounds.Width > 0 ? container.Bounds.Width : 240;
                double height = container?.Bounds.Height > 0 ? container.Bounds.Height : 100;

                if (pos.X >= scene.X && pos.X <= scene.X + width &&
                    pos.Y >= scene.Y && pos.Y <= scene.Y + height)
                {
                    return scene;
                }
            }
            return null;
        }

        private void OnAddChoiceClick(object? sender, RoutedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel scene)
            {
                scene.Choices.Add(new ChoiceModel());
            }
        }

        private void OnRemoveChoiceClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChoiceModel choice && ScenesListBox.SelectedItem is SceneModel scene)
            {
                scene.Choices.Remove(choice);
                UpdateConnectionLine();
            }
        }

        private void OnAnyFieldChanged(object? sender, RoutedEventArgs e)
        {
            UpdateConnectionLine();
        }

        // Настройка пресета для новых нодов
        private void ConfigureNewNode(SceneModel newScene, string nodeType)
        {
            newScene.NodeType = nodeType;

            switch (nodeType)
            {
                case "Choice":
                    newScene.Name = $"Выбор {Scenes.Count + 1}";
                    newScene.Text = "Варианты выбора...";
                    newScene.HeaderColor = "#ffc107";
                    break;
                case "Action":
                    newScene.Name = $"Действие {Scenes.Count + 1}";
                    newScene.Text = "Системный скрипт...";
                    newScene.CharacterName = "Система";
                    newScene.HeaderColor = "#e83e8c";
                    break;
                case "Dialogue":
                default:
                    newScene.Name = $"Сцена {Scenes.Count + 1}";
                    newScene.Text = "Новый диалог...";
                    newScene.HeaderColor = "#007acc";
                    break;
            }
        }

        private void UpdateConnectionLine()
        {
            if (ConnectionLine == null) return;
            var geometry = new Avalonia.Media.PathGeometry();

            // Локальная функция для отрисовки кривой Безье, чтобы не дублировать код
            void DrawBezierLine(SceneModel source, SceneModel target)
            {
                double outX = source.X + 240;
                double outY = source.Y + 80;
                double inX = target.X;
                double inY = target.Y + 80;

                double tension = Math.Max(Math.Abs(inX - outX) / 2, 50);

                var figure = new Avalonia.Media.PathFigure { StartPoint = new Point(outX, outY), IsClosed = false };
                var segment = new Avalonia.Media.BezierSegment
                {
                    Point1 = new Point(outX + tension, outY),
                    Point2 = new Point(inX - tension, inY),
                    Point3 = new Point(inX, inY)
                };
                figure.Segments.Add(segment);
                geometry.Figures.Add(figure);
            }

            foreach (var scene in Scenes)
            {
                // 1. Рисуем прямые связи (Для диалогов и действий)
                if (!string.IsNullOrWhiteSpace(scene.TargetSceneName))
                {
                    var target = Scenes.FirstOrDefault(s => s.Name == scene.TargetSceneName);
                    if (target != null && target != scene) DrawBezierLine(scene, target);
                }

                // 2. Рисуем связи от кнопок (Для узлов выбора)
                foreach (var choice in scene.Choices)
                {
                    var target = Scenes.FirstOrDefault(s => s.Name == choice.TargetSceneName);
                    if (target != null && target != scene) DrawBezierLine(scene, target);
                }
            }

            ConnectionLine.Data = geometry;
        }

        // ==========================================
        //         ЛОГИКА ИНТЕРФЕЙСА И ДВИЖКА
        // ==========================================

        private void OnEngineWindowClicked()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ScenesListBox.SelectedItem is SceneModel currentScene)
                {
                    // Если у нода есть прямой переход, прыгаем по нему (и проходим цепочку экшенов)
                    if (!string.IsNullOrWhiteSpace(currentScene.TargetSceneName))
                    {
                        var nextScene = Scenes.FirstOrDefault(s => s.Name == currentScene.TargetSceneName);
                        if (nextScene != null)
                        {
                            NavigateToScene(nextScene);
                            return;
                        }
                    }

                    // Если прямого перехода нет, но это не окно выбора, идем просто к следующему ноду в списке
                    if (currentScene.NodeType != "Choice" && Scenes.Count > 0)
                    {
                        int currentIndex = ScenesListBox.SelectedIndex;
                        int nextIndex = currentIndex + 1;
                        if (nextIndex < Scenes.Count)
                        {
                            NavigateToScene(Scenes[nextIndex]);
                        }
                    }
                }
            });
        }

        private void OnChoiceClicked(int choiceIndex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ScenesListBox.SelectedItem is SceneModel selectedScene && choiceIndex >= 0 && choiceIndex < selectedScene.Choices.Count)
                {
                    string targetName = selectedScene.Choices[choiceIndex].TargetSceneName;
                    var nextScene = Scenes.FirstOrDefault(s => s.Name == targetName);
                    if (nextScene != null)
                    {
                        NavigateToScene(nextScene);
                    }
                }
            });
        }

        private void NavigateToScene(SceneModel scene, System.Collections.Generic.HashSet<string>? visited = null)
        {
            visited ??= new System.Collections.Generic.HashSet<string>();
            if (!visited.Add(scene.Id))
            {
                Console.WriteLine("Обнаружена зацикленная цепочка системных нодов — остановлено во избежание зависания.");
                return;
            }

            ScenesListBox.SelectedItem = scene;
            SendSceneToEngine(scene);

            if (scene.NodeType == "Action")
            {
                ExecuteSystemCommand(scene);

                if (!string.IsNullOrWhiteSpace(scene.TargetSceneName))
                {
                    var next = Scenes.FirstOrDefault(s => s.Name == scene.TargetSceneName);
                    if (next != null) NavigateToScene(next, visited);
                }
            }
        }

        private void ExecuteSystemCommand(SceneModel scene)
        {
            if (!_isEngineRunning || string.IsNullOrWhiteSpace(scene.SystemCommand)) return;

            var parts = scene.SystemCommand.Split(':', 2);
            string command = parts[0].Trim().ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                switch (command)
                {
                    case "playsound":
                    case "playmusic":
                        if (!string.IsNullOrEmpty(arg)) Engine.PlayMusic(arg);
                        break;

                    case "stopmusic":
                    case "stopsound":
                        Engine.StopMusic();
                        break;

                    case "volume":
                        if (float.TryParse(arg, out float vol)) Engine.SetMusicVolume(vol);
                        break;

                    case "fadeout":
                        float duration = float.TryParse(arg, out float d) ? d : 2f;
                        Engine.StartMusicFadeOut(duration);
                        break;

                    case "fontsize":
                        if (int.TryParse(arg, out int size)) Engine.SetFontSize(size);
                        break;

                    default:
                        Console.WriteLine($"Неизвестная системная команда: '{scene.SystemCommand}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка выполнения системной команды '{scene.SystemCommand}': {ex.Message}");
            }
        }

        private void OnSaveProjectClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText("story.novel", JsonSerializer.Serialize(Scenes, options));
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        private void OnLoadProjectClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists("story.novel"))
                {
                    var loadedScenes = JsonSerializer.Deserialize<ObservableCollection<SceneModel>>(File.ReadAllText("story.novel"));
                    if (loadedScenes != null)
                    {
                        Scenes.Clear();
                        foreach (var s in loadedScenes) Scenes.Add(s);
                        if (Scenes.Count > 0) ScenesListBox.SelectedIndex = 0;
                        UpdateConnectionLine();
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
        }

        private void OnDeleteSceneClick(object? sender, RoutedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected && Scenes.Count > 1)
            {
                int index = Scenes.IndexOf(selected);
                Scenes.Remove(selected);
                ScenesListBox.SelectedIndex = Math.Max(0, index - 1);
                UpdateConnectionLine();
            }
        }

        private void OnSceneSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                TriggerEngineUpdate(musicChanged: true);
            }
        }

        private void OnFontSizeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int size = (int)e.NewValue;
            if (FontSizeTxt != null) FontSizeTxt.Text = $"{size}px";
            if (_isEngineRunning) Engine.SetFontSize(size);
        }

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
                    Engine.InitEngine(1024, 576, "Engine Runtime Core", AppDomain.CurrentDomain.BaseDirectory);
                    Engine.SetFontSize(initialFontSize);

                    SendSceneToEngine(selected);
                    if (!string.IsNullOrEmpty(selected.Music)) Engine.PlayMusic(selected.Music);
                    if (selected.NodeType == "Action") ExecuteSystemCommand(selected);

                    while (_isEngineRunning)
                    {
                        lock (_syncLock)
                        {
                            if (_needsSceneUpdate)
                            {
                                Engine.UpdateScene(_pendingBg, _pendingText, _pendingCharName, _pendingCharSprite, _pendingCharX, _pendingCharY);
                                if (_musicChanged)
                                {
                                    if (!string.IsNullOrEmpty(_pendingMusic)) Engine.PlayMusic(_pendingMusic);
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

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            _isEngineRunning = false;
            base.OnClosing(e);
        }
    }
}