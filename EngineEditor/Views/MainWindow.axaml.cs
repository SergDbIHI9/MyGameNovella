using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Avalonia.Threading;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using System.Threading;
using System.Globalization;
using System.Diagnostics;
using EngineEditor.Models;
using Avalonia.Platform.Storage;
using System.IO;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;

namespace EngineEditor.Views
{
    public partial class MainWindow : Window
    {
        private static Engine.ChoiceClickedCallback? _choiceCallbackDelegate;
        private static Engine.WindowClickedCallback? _clickCallbackDelegate;
        private static Engine.HotkeyCallback? _hotkeyCallbackDelegate;
        private static Engine.UiActionCallback? _uiActionCallbackDelegate;

        public ConcurrentDictionary<string, float> GameVariables = new();
        private readonly HashSet<string> _seenDialogueSceneIds = new(StringComparer.Ordinal);
        private volatile bool _isEngineRunning = false;
        private readonly ConcurrentQueue<Action> _runtimeActions = new();
        private readonly byte[] _nativeErrorBuffer = new byte[4096];
        private bool _nativeErrorApiSupported = true;
        private bool _nativeRevealApiSupported = true;
        private bool _nativeGameUiApiSupported = true;
        private bool _nativeEngineAvailable = true;
        private int _gameUiState;
        private bool _skipEnabled;
        private bool _skipHeld;

        public ObservableCollection<SceneModel> Scenes { get; set; } = new ObservableCollection<SceneModel>();
        public ObservableCollection<string> ProjectAssets { get; } = new();
        public ObservableCollection<string> ConsoleMessages { get; } = new();
        private readonly ObservableCollection<SceneModel> _filteredScenes = new();
        private readonly EditorUndoHistory<string> _undoHistory = new(100);
        private string? _inspectorEditSnapshot;
        private string? _dragStartSnapshot;
        private bool _restoringEditorState;
        private bool _applyingWorkspaceSettings;
        private Bitmap? _assetPreviewBitmap;
        private string? _selectedAudioPath;
        private IReadOnlyList<EditorAssetInfo> _allAssets = Array.Empty<EditorAssetInfo>();
        private const uint PlaySoundAsync = 0x0001;
        private const uint PlaySoundFilename = 0x00020000;

        [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PlayWaveFile(string? fileName, IntPtr module, uint flags);

        private readonly ScaleTransform _canvasScale = new ScaleTransform(1, 1);
        private readonly TranslateTransform _canvasPan = new TranslateTransform(0, 0);

        private double _zoom = 1.0;
        private bool _isPanningCanvas = false;
        private Point _lastPanPoint;

        private bool _isDraggingNode = false;
        private Point _dragOffset;
        private SceneModel? _draggedScene = null;
        private Control? _draggedContainer = null;

        private bool _isLinkingNodes = false;
        private SceneModel? _linkSourceScene = null;

        private bool _isDraggingFromPalette = false;
        private string? _pendingNodeType = null;
        private string? _currentProjectPath;
        private bool _isPlayerMode;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(string? playProjectPath)
        {
            _applyingWorkspaceSettings = true;
            InitializeComponent();
            DataContext = this;
            LoadWorkspaceSettings();
            InspectorTabs.AddHandler(InputElement.GotFocusEvent, OnInspectorGotFocus, RoutingStrategies.Bubble);
            InspectorTabs.AddHandler(InputElement.LostFocusEvent, OnInspectorLostFocus, RoutingStrategies.Bubble);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(_canvasScale);
            transformGroup.Children.Add(_canvasPan);
            GraphCanvas.RenderTransform = transformGroup;

            InitEngineCallback();

            Scenes.Add(new SceneModel { Name = "Сцена 1: Старт", Text = "Текст первого диалога...", X = 50, Y = 100, HeaderColor = "#6478E8" });
            Scenes.Add(new SceneModel { Name = "Сцена 2: Выбор", Text = "Куда пойдем?", X = 400, Y = 250, HeaderColor = "#D7A541" });
            Scenes[0].Choices.Add(new ChoiceModel { Text = "Далее", TargetSceneName = Scenes[1].Name, TargetSceneId = Scenes[1].Id });

            ScenesListBox.ItemsSource = _filteredScenes;
            ScenesListBox.SelectedIndex = 0;
            RefreshFilteredScenes();

            UpdateConnectionLine();

            if (!string.IsNullOrWhiteSpace(playProjectPath))
            {
                try
                {
                    var scenes = ProjectFileService.Load(File.ReadAllText(playProjectPath, Encoding.UTF8));
                    Scenes.Clear();
                    foreach (var scene in scenes)
                        Scenes.Add(scene);
                    _undoHistory.Clear();
                    UpdateUndoButtons();
                    RefreshFilteredScenes();

                    _currentProjectPath = Path.GetFullPath(playProjectPath);
                    _isPlayerMode = true;
                    RefreshProjectAssets();
                    ShowInTaskbar = false;
                    Opened += OnPlayerWindowOpened;
                    SetProjectStatus($"Запуск игры: {_currentProjectPath}", isError: false);
                }

                catch (Exception ex)
                {
                    SetProjectStatus($"Не удалось открыть игру «{playProjectPath}»: {ex.Message}", isError: true);
                }
            }
        }

        private void LoadWorkspaceSettings()
        {
            var settings = EditorWorkspaceSettings.Load(onError: message =>
                SetProjectStatus($"Не удалось прочитать раскладку редактора; применены значения по умолчанию: {message}", isError: true));
            _applyingWorkspaceSettings = true;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(settings.HierarchyWidth);
            WorkspaceGrid.ColumnDefinitions[4].Width = new GridLength(settings.InspectorWidth);
            WorkspaceGrid.RowDefinitions[3].Height = new GridLength(settings.BottomHeight);
            if (settings.PositionX != int.MinValue
                && Screens.All.Any(screen => screen.Bounds.Contains(new PixelPoint(settings.PositionX, settings.PositionY))))
                Position = new PixelPoint(settings.PositionX, settings.PositionY);
            InspectorTabs.SelectedIndex = settings.InspectorTab;
            BottomTabs.SelectedIndex = settings.BottomTab;
            _applyingWorkspaceSettings = false;
        }

        private void SaveWorkspaceSettings()
        {
            if (_applyingWorkspaceSettings || WorkspaceGrid is null || InspectorTabs is null
                || BottomTabs is null || WindowState != WindowState.Normal)
                return;
            try
            {
                var settings = new EditorWorkspaceSettings
                {
                    WindowWidth = Width,
                    WindowHeight = Height,
                    PositionX = Position.X,
                    PositionY = Position.Y,
                    HierarchyWidth = WorkspaceGrid.ColumnDefinitions[0].ActualWidth,
                    InspectorWidth = WorkspaceGrid.ColumnDefinitions[4].ActualWidth,
                    BottomHeight = WorkspaceGrid.RowDefinitions[3].ActualHeight,
                    InspectorTab = InspectorTabs.SelectedIndex,
                    BottomTab = BottomTabs.SelectedIndex
                };
                if (!settings.IsValid())
                    return;
                settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                SetProjectStatus($"Не удалось сохранить раскладку редактора: {ex.Message}", isError: true);
            }
        }

        private void OnWorkspaceSplitterDragCompleted(object? sender, VectorEventArgs e) => SaveWorkspaceSettings();

        private void OnWorkspaceTabChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (!_applyingWorkspaceSettings)
                SaveWorkspaceSettings();
        }

        private void OnPlayerWindowOpened(object? sender, EventArgs e)
        {
            if (!_nativeEngineAvailable)
            {
                ShowPlayerError("Игра загружена, но запуск недоступен: рядом с приложением отсутствует EngineCore.dll или её зависимости.");
                return;
            }
            Hide();
            Dispatcher.UIThread.Post(() =>
            {
                if (ScenesListBox.SelectedItem is null)
                    ScenesListBox.SelectedIndex = 0;
                OnStartEngineClick(this, new RoutedEventArgs());
            });
        }

        public void InitEngineCallback()
        {
            try
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
            catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
            {
                _nativeEngineAvailable = false;
                SetProjectStatus($"Нативный preview отключён: EngineCore.dll или её зависимости недоступны. Редактор проектов продолжит работу. {ex.Message}", isError: true);
                return;
            }

            if (_hotkeyCallbackDelegate == null)
            {
                _hotkeyCallbackDelegate = OnGameHotkey;
                try
                {
                    Engine.RegisterHotkeyCallback(_hotkeyCallbackDelegate);
                }
                catch (EntryPointNotFoundException)
                {
                    SetProjectStatus("EngineCore.dll устарела: горячие клавиши сохранения недоступны. Пересоберите нативное ядро.", isError: true);
                }
            }

            if (_uiActionCallbackDelegate == null)
            {
                _uiActionCallbackDelegate = OnGameUiAction;
                try
                {
                    Engine.RegisterUiActionCallback(_uiActionCallbackDelegate);
                }
                catch (EntryPointNotFoundException)
                {
                    _nativeGameUiApiSupported = false;
                    SetProjectStatus("EngineCore.dll устарела: меню игры недоступно. Пересоберите нативное ядро.", isError: true);
                }
            }
        }

        private void OnGameHotkey(int hotkey)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (hotkey is >= 100 and < 100 + GameSaveService.SlotCount)
                    SaveGameProgress(hotkey - 100);
                else if (hotkey is >= 200 and < 200 + GameSaveService.SlotCount)
                    LoadGameProgress(hotkey - 200);
            });
        }

        private void OnGameUiAction(int action)
        {
            Dispatcher.UIThread.Post(() =>
            {
                switch (action)
                {
                    case 0:
                        ShowSaveSlots();
                        break;
                    case 1:
                        ShowLoadSlots();
                        break;
                    case 2:
                        ToggleGameUiState(8);
                        break;
                    case 3:
                        ToggleGameUiState(2);
                        break;
                    case 4:
                        ToggleGameUiState(1);
                        break;
                    case 5:
                        _skipEnabled = !_skipEnabled;
                        UpdateNativeGameUiState();
                        break;
                    case 6:
                        _skipHeld = true;
                        UpdateNativeGameUiState();
                        break;
                    case 7:
                        _skipHeld = false;
                        UpdateNativeGameUiState();
                        break;
                    case 8:
                        QueueRuntimeAction(() => Engine.SetSaveMenuState(0));
                        break;
                    default:
                        if (action is >= 100 and < 100 + GameSaveService.SlotCount)
                            SaveGameProgress(action - 100);
                        else if (action is >= 200 and < 200 + GameSaveService.SlotCount)
                            LoadGameProgress(action - 200);
                        break;
                }
            });
        }

        private void ToggleGameUiState(int flag)
        {
            _gameUiState ^= flag;
            UpdateNativeGameUiState();
        }

        private void UpdateNativeGameUiState()
        {
            int state = _gameUiState;
            if (_skipEnabled || _skipHeld)
                state |= 4;
            else
                state &= ~4;

            if (_nativeGameUiApiSupported)
                QueueRuntimeAction(() => Engine.SetGameUiState(state));
        }

        private void ShowSaveSlots()
        {
            ShowSaveSlotDialog(isSaving: true);
        }

        private void ShowLoadSlots()
        {
            ShowSaveSlotDialog(isSaving: false);
        }

        private void ShowSaveSlotDialog(bool isSaving)
        {
            if (_currentProjectPath is null)
            {
                ShowRuntimeDiagnostic("Слоты сохранения доступны после открытия игрового проекта.");
                return;
            }

            var slotDescriptions = new (string Description, bool Exists)[GameSaveService.SlotCount];
            for (int slotIndex = 0; slotIndex < GameSaveService.SlotCount; slotIndex++)
            {
                string slotPath = GameSaveService.GetSlotSavePath(_currentProjectPath, slotIndex);
                string description = isSaving ? "Пустой слот — нажмите, чтобы сохранить здесь." : "Пустой слот.";
                bool exists = false;

                if (File.Exists(slotPath))
                {
                    try
                    {
                        GameSaveData saved = GameSaveService.Load(slotPath, Scenes);
                        SceneModel savedScene = Scenes.Single(scene => scene.Id == saved.SceneId);
                        string savedAt = saved.SavedAtUtc == default
                            ? File.GetLastWriteTime(slotPath).ToString("g")
                            : saved.SavedAtUtc.LocalDateTime.ToString("g");
                        description = $"{savedScene.Name}  |  {savedAt}";
                        exists = true;
                    }
                    catch (Exception ex) when (ex is IOException or InvalidDataException
                        or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                    {
                        description = $"Слот повреждён или не подходит этому проекту: {ex.Message}";
                        exists = true;
                    }
                }

                slotDescriptions[slotIndex] = (description, exists);
            }

            int mode = isSaving ? 1 : 2;
            QueueRuntimeAction(() =>
            {
                for (int slotIndex = 0; slotIndex < slotDescriptions.Length; slotIndex++)
                {
                    var slot = slotDescriptions[slotIndex];
                    Engine.SetSaveSlotInfo(slotIndex, slot.Description, slot.Exists);
                }
                Engine.SetSaveMenuState(mode);
            });
        }

        private void SaveGameProgress(int slotIndex)
        {
            if (_currentProjectPath is null || ScenesListBox.SelectedItem is not SceneModel scene)
            {
                ShowRuntimeDiagnostic("Невозможно сохранить игру: сначала откройте проект и выберите сцену.");
                return;
            }

            try
            {
                string savePath = GameSaveService.GetSlotSavePath(_currentProjectPath, slotIndex);
                GameSaveService.Save(savePath, scene.Id, GameVariables, _seenDialogueSceneIds);
                QueueRuntimeAction(() => Engine.SetSaveMenuState(0));
                SetProjectStatus($"Игра сохранена в слот {slotIndex + 1}: {savePath}", isError: false);
            }
            catch (Exception ex)
            {
                ShowRuntimeDiagnostic($"Не удалось сохранить игру: {ex.Message}");
            }
        }

        private bool LoadGameProgress(int slotIndex)
        {
            if (_currentProjectPath is null)
            {
                ShowRuntimeDiagnostic("Невозможно загрузить игру: проект не открыт.");
                return false;
            }

            try
            {
                string savePath = GameSaveService.GetSlotSavePath(_currentProjectPath, slotIndex);
                GameSaveData save = GameSaveService.Load(savePath, Scenes);
                GameVariables.Clear();
                foreach (var (name, value) in save.Variables)
                    GameVariables[name] = value;
                _seenDialogueSceneIds.Clear();
                foreach (string sceneId in save.SeenSceneIds)
                    _seenDialogueSceneIds.Add(sceneId);

                SceneModel savedScene = Scenes.Single(scene => scene.Id == save.SceneId);
                NavigateToScene(savedScene);
                QueueRuntimeAction(() => Engine.SetSaveMenuState(0));
                SetProjectStatus($"Слот {slotIndex + 1} загружен.", isError: false);
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException
                or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                ShowRuntimeDiagnostic($"Не удалось загрузить сохранение: {ex.Message}");
                return false;
            }
        }

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

                _canvasPan.X = pointerPos.X - (pointerPos.X - _canvasPan.X) * actualZoomDelta;
                _canvasPan.Y = pointerPos.Y - (pointerPos.Y - _canvasPan.Y) * actualZoomDelta;
            }
        }

        private void OnSceneSearchChanged(object? sender, TextChangedEventArgs e)
        {
            RefreshFilteredScenes();
        }

        private void RefreshFilteredScenes()
        {
            string query = SceneSearchBox?.Text?.Trim() ?? "";
            string? selectedId = (ScenesListBox?.SelectedItem as SceneModel)?.Id;
            _filteredScenes.Clear();
            foreach (var scene in Scenes.Where(scene => EditorSceneSearch.Matches(scene, query)))
                _filteredScenes.Add(scene);

            if (ScenesListBox is null)
                return;
            if (selectedId is not null)
                ScenesListBox.SelectedItem = _filteredScenes.FirstOrDefault(scene => scene.Id == selectedId);
            if (ScenesListBox.SelectedItem is null && _filteredScenes.Count > 0)
                ScenesListBox.SelectedIndex = 0;
        }

        private void UpdateSelectedSceneDiagnostics()
        {
            if (ScenesListBox?.SelectedItem is not SceneModel selected)
                return;

            selected.SelectedSceneDiagnostics.Clear();
            var diagnostics = StoryProjectValidator.Validate(Scenes, GetProjectAssetDirectory());
            foreach (var diagnostic in diagnostics.Where(item =>
                         item.Message.Contains($"«{selected.Name}»", StringComparison.Ordinal)
                         || item.Message.Contains($"'{selected.Name}'", StringComparison.Ordinal)))
                selected.SelectedSceneDiagnostics.Add(
                    $"{(diagnostic.Severity == StoryDiagnosticSeverity.Error ? "ОШИБКА" : "ПРЕДУПРЕЖДЕНИЕ")}: {diagnostic.Message}");
        }

        private string CaptureSceneSnapshot() => JsonSerializer.Serialize(Scenes);

        private void RecordUndoSnapshot(string snapshot)
        {
            if (!_restoringEditorState && !string.Equals(snapshot, CaptureSceneSnapshot(), StringComparison.Ordinal))
            {
                _undoHistory.Record(snapshot);
                UpdateUndoButtons();
            }
        }

        private void UpdateUndoButtons()
        {
            if (UndoButton is not null)
                UndoButton.IsEnabled = _undoHistory.CanUndo;
            if (RedoButton is not null)
                RedoButton.IsEnabled = _undoHistory.CanRedo;
        }

        private void OnInspectorGotFocus(object? sender, RoutedEventArgs e)
        {
            if (e.Source is TextBox && _inspectorEditSnapshot is null)
                _inspectorEditSnapshot = CaptureSceneSnapshot();
        }

        private void OnInspectorLostFocus(object? sender, RoutedEventArgs e)
        {
            if (e.Source is not TextBox || _inspectorEditSnapshot is null)
                return;
            RecordUndoSnapshot(_inspectorEditSnapshot);
            _inspectorEditSnapshot = null;
            RefreshFilteredScenes();
            UpdateConnectionLine();
            UpdateSelectedSceneDiagnostics();
        }

        private void OnUndoClick(object? sender, RoutedEventArgs e) => Undo();
        private void OnRedoClick(object? sender, RoutedEventArgs e) => Redo();

        private void Undo()
        {
            string current = CaptureSceneSnapshot();
            if (_undoHistory.TryUndo(current, out string previous))
            {
                RestoreSceneSnapshot(previous);
                UpdateUndoButtons();
            }
        }

        private void Redo()
        {
            string current = CaptureSceneSnapshot();
            if (_undoHistory.TryRedo(current, out string next))
            {
                RestoreSceneSnapshot(next);
                UpdateUndoButtons();
            }
        }

        private void RestoreSceneSnapshot(string snapshot)
        {
            try
            {
                var restored = JsonSerializer.Deserialize<ObservableCollection<SceneModel>>(snapshot)
                    ?? throw new InvalidDataException("Снимок сцены пуст.");
                _restoringEditorState = true;
                string? selectedId = (ScenesListBox.SelectedItem as SceneModel)?.Id;
                Scenes.Clear();
                foreach (var scene in restored)
                    Scenes.Add(scene);
                RefreshReferenceNames();
                RefreshFilteredScenes();
                ScenesListBox.SelectedItem = Scenes.FirstOrDefault(scene => scene.Id == selectedId)
                    ?? _filteredScenes.FirstOrDefault();
                UpdateConnectionLine();
                UpdateSelectedSceneDiagnostics();
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                SetProjectStatus($"Не удалось восстановить историю изменений: {ex.Message}", isError: true);
                _undoHistory.Clear();
                UpdateUndoButtons();
            }
            finally
            {
                _restoringEditorState = false;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Z)
            {
                if (e.Source is not TextBox)
                {
                    Undo();
                    e.Handled = true;
                }
            }
            else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.Y)
            {
                if (e.Source is not TextBox)
                {
                    Redo();
                    e.Handled = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed)
            {
                _isPanningCanvas = true;
                _lastPanPoint = e.GetPosition(this);
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
                _dragStartSnapshot = CaptureSceneSnapshot();

                var pos = e.GetPosition(GraphCanvas);
                _dragOffset = new Point(pos.X - scene.X, pos.Y - scene.Y);

                e.Pointer.Capture(border);
                e.Handled = true;
            }
        }

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

            if (_isDraggingFromPalette && _pendingNodeType != null)
            {
                var pos = e.GetPosition(GraphCanvas);

                var newScene = new SceneModel { X = pos.X - 120, Y = pos.Y - 20 };
                ConfigureNewNode(newScene, _pendingNodeType);

                _dragStartSnapshot = CaptureSceneSnapshot();
                Scenes.Add(newScene);
                RefreshFilteredScenes();
                ScenesListBox.SelectedItem = newScene;

                _draggedScene = newScene;
                _isDraggingNode = true;
                _dragOffset = new Point(120, 20);

                _isDraggingFromPalette = false;
                _pendingNodeType = null;
            }

            if (_isDraggingNode && _draggedScene != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                _draggedScene.X = pos.X - _dragOffset.X;
                _draggedScene.Y = pos.Y - _dragOffset.Y;

                _draggedContainer ??= NodesItemsControl.ContainerFromItem(_draggedScene);
                if (_draggedContainer != null)
                {
                    Canvas.SetLeft(_draggedContainer, _draggedScene.X);
                    Canvas.SetTop(_draggedContainer, _draggedScene.Y);
                }

                UpdateConnectionLine();
            }

            if (_isLinkingNodes && _linkSourceScene != null && TempLinkLine != null)
            {
                var pos = e.GetPosition(GraphCanvas);

                double outX = _linkSourceScene.X + 240;
                double outY = _linkSourceScene.Y + 80;

                var geometry = new Avalonia.Media.PathGeometry();
                var figure = new Avalonia.Media.PathFigure
                {
                    StartPoint = new Point(outX, outY),
                    IsClosed = false,
                    Segments = new Avalonia.Media.PathSegments
                    {
                        new Avalonia.Media.LineSegment { Point = pos }
                    }
                };
                geometry.Figures = new Avalonia.Media.PathFigures { figure };
                TempLinkLine.Data = geometry;
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            if (_isPanningCanvas)
            {
                _isPanningCanvas = false;
                Cursor = new Cursor(StandardCursorType.Arrow);
                e.Handled = true;
                return;
            }

            if (_isDraggingFromPalette && _pendingNodeType != null)
            {
                var newScene = new SceneModel { X = 300, Y = 200 };
                ConfigureNewNode(newScene, _pendingNodeType);

                _dragStartSnapshot = CaptureSceneSnapshot();
                Scenes.Add(newScene);
                RefreshFilteredScenes();
                ScenesListBox.SelectedItem = newScene;

                _isDraggingFromPalette = false;
                _pendingNodeType = null;
                if (_dragStartSnapshot is not null)
                    RecordUndoSnapshot(_dragStartSnapshot);
                _dragStartSnapshot = null;
            }

            if (_isDraggingNode)
            {
                _isDraggingNode = false;
                _draggedScene = null;
                _draggedContainer = null;
                if (_dragStartSnapshot is not null)
                    RecordUndoSnapshot(_dragStartSnapshot);
                _dragStartSnapshot = null;
            }

            if (_isLinkingNodes && _linkSourceScene != null)
            {
                var pos = e.GetPosition(GraphCanvas);
                var target = FindNodeAt(pos, exclude: _linkSourceScene);

                if (target != null)
                {
                    string beforeLink = CaptureSceneSnapshot();
                    if (_linkSourceScene.NodeType == "Choice")
                    {
                        _linkSourceScene.Choices.Add(new ChoiceModel { Text = "Вариант", TargetSceneName = target.Name, TargetSceneId = target.Id });
                    }
                    else
                    {
                        _linkSourceScene.TargetSceneName = target.Name;
                        _linkSourceScene.TargetSceneId = target.Id;
                    }
                    RecordUndoSnapshot(beforeLink);
                    UpdateConnectionLine();
                }

                _isLinkingNodes = false;
                _linkSourceScene = null;
                if (TempLinkLine != null) TempLinkLine.Data = null;
            }

            e.Pointer.Capture(null);
        }

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
                string before = CaptureSceneSnapshot();
                scene.Choices.Add(new ChoiceModel());
                RecordUndoSnapshot(before);
            }
        }

        private void OnRemoveChoiceClick(object? sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ChoiceModel choice && ScenesListBox.SelectedItem is SceneModel scene)
            {
                string before = CaptureSceneSnapshot();
                scene.Choices.Remove(choice);
                RecordUndoSnapshot(before);
                UpdateConnectionLine();
            }
        }

        private void OnAnyFieldChanged(object? sender, RoutedEventArgs e)
        {
            UpdateConnectionLine();
        }

        private SceneModel? FindReferencedScene(string? id, string? name)
        {
            return StoryRuntimeLogic.ResolveTarget(Scenes, id, name);
        }

        private void OnSceneNameChanged(object? sender, RoutedEventArgs e)
        {
            RefreshReferenceNames();
            UpdateConnectionLine();
        }

        private void RefreshReferenceNames()
        {
            var byId = Scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
            foreach (var scene in Scenes)
            {
                if (byId.TryGetValue(scene.TargetSceneId, out var target))
                    scene.TargetSceneName = target.Name;
                if (byId.TryGetValue(scene.TargetSceneIfTrueId, out target))
                    scene.TargetSceneIfTrue = target.Name;
                if (byId.TryGetValue(scene.TargetSceneIfFalseId, out target))
                    scene.TargetSceneIfFalse = target.Name;
                foreach (var choice in scene.Choices)
                {
                    if (byId.TryGetValue(choice.TargetSceneId, out target))
                        choice.TargetSceneName = target.Name;
                }
            }
        }

        private void OnTargetSceneNameChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: SceneModel scene })
                ResolveEditedReference(scene.Name, scene.TargetSceneName, id => scene.TargetSceneId = id);
            UpdateConnectionLine();
        }

        private void OnTargetSceneIfTrueChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: SceneModel scene })
                ResolveEditedReference(scene.Name, scene.TargetSceneIfTrue, id => scene.TargetSceneIfTrueId = id);
            UpdateConnectionLine();
        }

        private void OnTargetSceneIfFalseChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: SceneModel scene })
                ResolveEditedReference(scene.Name, scene.TargetSceneIfFalse, id => scene.TargetSceneIfFalseId = id);
            UpdateConnectionLine();
        }

        private void OnChoiceTargetSceneNameChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { DataContext: ChoiceModel choice })
                ResolveEditedReference("choice", choice.TargetSceneName, id => choice.TargetSceneId = id);
            UpdateConnectionLine();
        }

        private void ResolveEditedReference(string sourceName, string targetName, Action<string> setId)
        {
            if (string.IsNullOrWhiteSpace(targetName))
            {
                setId("");
                SetProjectStatus("Переход очищен.", isError: false);
                return;
            }

            var matches = Scenes.Where(scene => scene.Name == targetName).ToList();
            if (matches.Count == 1)
            {
                setId(matches[0].Id);
                SetProjectStatus($"Переход из «{sourceName}» связан со сценой «{matches[0].Name}».", isError: false);
            }
            else
            {
                setId("");
                SetProjectStatus(matches.Count == 0
                    ? $"Не найдена целевая сцена «{targetName}». Исправьте ссылку перед сохранением."
                    : $"Имя сцены «{targetName}» неоднозначно. Имена сцен должны быть уникальны для ссылок по имени.",
                    isError: true);
            }
        }

        private void SetProjectStatus(string message, bool isError)
        {
            string prefix = isError ? "ERROR" : "INFO";
            ConsoleMessages.Add($"[{DateTime.Now:HH:mm:ss}] [{prefix}] {message}");
            while (ConsoleMessages.Count > 300)
                ConsoleMessages.RemoveAt(0);

            if (ProjectStatus is not null)
            {
                ProjectStatus.Text = message;
                ProjectStatus.Foreground = isError
                    ? new SolidColorBrush(Color.Parse("#ff6b6b"))
                    : new SolidColorBrush(Color.Parse("#93A4BA"));
            }

            if (CurrentProjectDisplay is not null)
                CurrentProjectDisplay.Text = _currentProjectPath ?? "Проект не открыт";
        }

        private void OnRefreshAssetsClick(object? sender, RoutedEventArgs e)
        {
            if (_currentProjectPath is null)
            {
                SetProjectStatus("Сначала откройте или сохраните проект.", isError: true);
                return;
            }

            RefreshProjectAssets();
        }

        private void OnOpenAssetsFolderClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentProjectPath is null)
                {
                    SetProjectStatus("Сначала откройте или сохраните проект.", isError: true);
                    return;
                }

                string assetsDirectory = Path.Combine(GetProjectAssetDirectory(), "Assets");
                Directory.CreateDirectory(assetsDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = assetsDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                SetProjectStatus($"Не удалось открыть папку ресурсов: {ex.Message}", isError: true);
            }
        }

        private void OnClearConsoleClick(object? sender, RoutedEventArgs e)
        {
            ConsoleMessages.Clear();
        }

        private void OnAssetFilterChanged(object? sender, RoutedEventArgs e) => ApplyAssetFilter();

        private void ApplyAssetFilter()
        {
            string query = AssetSearchBox?.Text?.Trim() ?? "";
            EditorAssetType type = AssetTypeFilter?.SelectedIndex switch
            {
                1 => EditorAssetType.Image,
                2 => EditorAssetType.Audio,
                3 => EditorAssetType.Other,
                _ => EditorAssetType.All
            };
            ProjectAssets.Clear();
            foreach (var asset in _allAssets.Where(asset =>
                         (type == EditorAssetType.All || asset.Type == type)
                         && (query.Length == 0 || asset.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))))
                ProjectAssets.Add(asset.RelativePath);
        }

        private void OnAssetSelected(object? sender, SelectionChangedEventArgs e)
        {
            StopAssetAudio();
            if (AssetListBox?.SelectedItem is not string relativePath)
            {
                ClearAssetPreview();
                return;
            }

            var asset = _allAssets.FirstOrDefault(item => item.RelativePath == relativePath);
            if (asset is null)
            {
                ClearAssetPreview();
                return;
            }

            AssetPreviewTitle.Text = relativePath;
            AudioPreviewControls.IsVisible = false;
            AssetPreviewImage.IsVisible = false;
            try
            {
                var details = new StringBuilder()
                    .AppendLine($"{asset.Type} • {asset.Length:N0} байт")
                    .Append($"Изменён: {asset.LastModifiedUtc.ToLocalTime():g}");

                if (asset.Type == EditorAssetType.Image)
                {
                    if (asset.Length > 64L * 1024 * 1024)
                        throw new InvalidDataException("Изображение больше 64 МБ; предпросмотр пропущен.");
                    DisposeAssetPreviewBitmap();
                    _assetPreviewBitmap = new Bitmap(asset.FullPath);
                    AssetPreviewImage.Source = _assetPreviewBitmap;
                    AssetPreviewImage.IsVisible = true;
                    details.AppendLine().Append($"Размер: {_assetPreviewBitmap.Size.Width} × {_assetPreviewBitmap.Size.Height}");
                }
                else if (asset.Type == EditorAssetType.Audio)
                {
                    if (string.Equals(Path.GetExtension(asset.FullPath), ".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        if (asset.Length > 64L * 1024 * 1024)
                            throw new InvalidDataException("WAV больше 64 МБ; предпросмотр пропущен.");
                        _selectedAudioPath = asset.FullPath;
                        AudioPreviewControls.IsVisible = true;
                        details.AppendLine().Append("Предпросмотр воспроизводит WAV.");
                    }
                    else
                    {
                        details.AppendLine().Append("Встроенный предпросмотр доступен только для WAV.");
                    }
                }

                AssetPreviewDetails.Text = details.ToString();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidDataException or ArgumentException or NotSupportedException)
            {
                AssetPreviewDetails.Text = $"Не удалось показать ресурс: {ex.Message}";
                SetProjectStatus($"Ошибка предпросмотра ресурса «{relativePath}»: {ex.Message}", isError: true);
            }
        }

        private void OnPlayAssetAudioClick(object? sender, RoutedEventArgs e)
        {
            if (_selectedAudioPath is null)
                return;
            try
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("Встроенный WAV-preview сейчас доступен только в Windows.");
                StopAssetAudio();
                if (!PlayWaveFile(_selectedAudioPath, IntPtr.Zero, PlaySoundAsync | PlaySoundFilename))
                    throw new IOException("Windows не смогла открыть WAV-файл.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or InvalidOperationException or DllNotFoundException or PlatformNotSupportedException)
            {
                SetProjectStatus($"Не удалось воспроизвести WAV: {ex.Message}", isError: true);
            }
        }

        private void OnStopAssetAudioClick(object? sender, RoutedEventArgs e) => StopAssetAudio();

        private void StopAssetAudio()
        {
            if (!OperatingSystem.IsWindows())
                return;
            try
            {
                PlayWaveFile(null, IntPtr.Zero, 0);
            }
            catch (DllNotFoundException)
            {
                SetProjectStatus("WAV-preview недоступен: не найдена библиотека Windows Multimedia.", isError: true);
            }
        }

        private void DisposeAssetPreviewBitmap()
        {
            AssetPreviewImage.Source = null;
            _assetPreviewBitmap?.Dispose();
            _assetPreviewBitmap = null;
        }

        private void ClearAssetPreview()
        {
            StopAssetAudio();
            DisposeAssetPreviewBitmap();
            _selectedAudioPath = null;
            if (AssetPreviewTitle is not null)
                AssetPreviewTitle.Text = "Выберите ресурс";
            if (AssetPreviewDetails is not null)
                AssetPreviewDetails.Text = "";
            if (AudioPreviewControls is not null)
                AudioPreviewControls.IsVisible = false;
        }

        private void RefreshProjectAssets()
        {
            ClearAssetPreview();
            ProjectAssets.Clear();
            _allAssets = Array.Empty<EditorAssetInfo>();
            if (_currentProjectPath is null)
                return;

            try
            {
                string projectDirectory = GetProjectAssetDirectory();
                string assetsDirectory = Path.Combine(projectDirectory, "Assets");
                _allAssets = EditorAssetBrowser.Find(assetsDirectory, projectDirectory);
                ApplyAssetFilter();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetProjectStatus($"Не удалось обновить список ресурсов: {ex.Message}", isError: true);
            }
        }

        private void ConfigureNewNode(SceneModel newScene, string nodeType)
        {
            newScene.NodeType = nodeType;

            switch (nodeType)
            {
                case "Choice":
                    newScene.Name = $"Выбор {Scenes.Count + 1}";
                    newScene.Text = "Варианты выбора...";
                    newScene.HeaderColor = "#D7A541";
                    break;
                case "Condition":
                    newScene.Name = $"Условие {Scenes.Count + 1}";
                    newScene.Text = "Системная проверка...";
                    newScene.HeaderColor = "#4AA6B4";
                    break;
                case "Action":
                    newScene.Name = $"Действие {Scenes.Count + 1}";
                    newScene.Text = "Системный скрипт...";
                    newScene.CharacterName = "Система";
                    newScene.HeaderColor = "#CF6C9C";
                    break;
                case "Dialogue":
                default:
                    newScene.Name = $"Сцена {Scenes.Count + 1}";
                    newScene.Text = "Новый диалог...";
                    newScene.HeaderColor = "#6478E8";
                    break;
            }
        }

        private void UpdateConnectionLine()
        {
            if (ConnectionLine == null) return;
            var figures = new Avalonia.Media.PathFigures();
            var geometry = new Avalonia.Media.PathGeometry { Figures = figures };

            void DrawBezierLine(SceneModel source, SceneModel target)
            {
                double outX = source.X + 240;
                double outY = source.Y + 80;
                double inX = target.X;
                double inY = target.Y + 80;

                double tension = Math.Max(Math.Abs(inX - outX) / 2, 50);

                var figure = new Avalonia.Media.PathFigure
                {
                    StartPoint = new Point(outX, outY),
                    IsClosed = false,
                    Segments = new Avalonia.Media.PathSegments
                    {
                        new Avalonia.Media.BezierSegment
                        {
                            Point1 = new Point(outX + tension, outY),
                            Point2 = new Point(inX - tension, inY),
                            Point3 = new Point(inX, inY)
                        }
                    }
                };
                figures.Add(figure);
            }

            foreach (var scene in Scenes)
            {
                if (!string.IsNullOrWhiteSpace(scene.TargetSceneName) || !string.IsNullOrWhiteSpace(scene.TargetSceneId))
                {
                    var target = FindReferencedScene(scene.TargetSceneId, scene.TargetSceneName);
                    if (target != null && target != scene) DrawBezierLine(scene, target);
                }

                foreach (var choice in scene.Choices)
                {
                    var target = FindReferencedScene(choice.TargetSceneId, choice.TargetSceneName);
                    if (target != null && target != scene) DrawBezierLine(scene, target);
                }

                if (scene.NodeType == "Condition")
                {
                    if (!string.IsNullOrWhiteSpace(scene.TargetSceneIfTrue) || !string.IsNullOrWhiteSpace(scene.TargetSceneIfTrueId))
                    {
                        var target = FindReferencedScene(scene.TargetSceneIfTrueId, scene.TargetSceneIfTrue);
                        if (target != null && target != scene) DrawBezierLine(scene, target);
                    }
                    if (!string.IsNullOrWhiteSpace(scene.TargetSceneIfFalse) || !string.IsNullOrWhiteSpace(scene.TargetSceneIfFalseId))
                    {
                        var target = FindReferencedScene(scene.TargetSceneIfFalseId, scene.TargetSceneIfFalse);
                        if (target != null && target != scene) DrawBezierLine(scene, target);
                    }
                }
            }

            ConnectionLine.Data = geometry;
        }

        private void OnEngineWindowClicked()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ScenesListBox.SelectedItem is SceneModel currentScene)
                {
                    if (currentScene.NodeType == "Choice")
                    {
                        SetProjectStatus("Выберите вариант кнопкой в окне игры.", isError: false);
                        return;
                    }

                    if (currentScene.NodeType != "Dialogue")
                        return;

                    try
                    {
                        var nextScene = FindReferencedScene(currentScene.TargetSceneId, currentScene.TargetSceneName);
                        if (nextScene is null)
                        {
                            QueueRuntimeAction(() => Engine.SetPlaybackState(true, true));
                            SetProjectStatus($"Сцена «{currentScene.Name}» завершена; у неё нет следующего перехода.", isError: false);
                            return;
                        }

                        NavigateToScene(nextScene);
                    }
                    catch (InvalidOperationException ex)
                    {
                        SetProjectStatus($"Ошибка перехода: {ex.Message}", isError: true);
                    }
                }
            });
        }

        private void OnChoiceClicked(int choiceIndex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ScenesListBox.SelectedItem is SceneModel { NodeType: "Choice" } selectedScene
                    && choiceIndex >= 0 && choiceIndex < Math.Min(selectedScene.Choices.Count, 4))
                {
                    var choice = selectedScene.Choices[choiceIndex];
                    try
                    {
                        var nextScene = FindReferencedScene(choice.TargetSceneId, choice.TargetSceneName);
                        if (nextScene is null)
                        {
                            SetProjectStatus($"Для варианта «{choice.Text}» не задан переход.", isError: true);
                            return;
                        }
                        NavigateToScene(nextScene);
                    }
                    catch (InvalidOperationException ex)
                    {
                        SetProjectStatus($"Ошибка перехода по выбору: {ex.Message}", isError: true);
                    }
                }
            });
        }

        private void NavigateToScene(SceneModel scene, HashSet<string>? visited = null)
        {
            visited ??= new HashSet<string>();

            if (!visited.Add(scene.Id))
            {
                Console.WriteLine("Обнаружена зацикленная цепочка системных нодов — остановлено во избежание зависания.");
                return;
            }

            ScenesListBox.SelectedItem = scene;
            bool sceneWasAlreadySeen = scene.NodeType == "Dialogue"
                && !_seenDialogueSceneIds.Add(scene.Id);
            SendSceneToEngine(
                scene,
                playMusic: true,
                recordHistory: true,
                wasSeen: sceneWasAlreadySeen);

            if (scene.NodeType == "Action")
            {
                ExecuteSystemCommand(scene.SystemCommand);
                if (!string.IsNullOrWhiteSpace(scene.LuaScript))
                {
                    _ = RunLuaScriptAsync(scene.LuaScript, () => ContinueAfterAction(scene, visited));
                    return;
                }

                ContinueAfterAction(scene, visited);
            }
            else if (scene.NodeType == "Condition")
            {
                bool isTrue;
                try
                {
                    isTrue = StoryRuntimeLogic.EvaluateCondition(scene, GameVariables);
                }
                catch (FormatException ex)
                {
                    SetProjectStatus(ex.Message, isError: true);
                    return;
                }

                string targetName = isTrue ? scene.TargetSceneIfTrue : scene.TargetSceneIfFalse;
                string targetId = isTrue ? scene.TargetSceneIfTrueId : scene.TargetSceneIfFalseId;
                try
                {
                    var next = FindReferencedScene(targetId, targetName);
                    if (next is not null)
                    {
                        NavigateToScene(next, visited);
                    }
                    else
                    {
                        SetProjectStatus(
                            $"Условие «{scene.Name}» выбрало ветвь {(isTrue ? "true" : "false")}, но переход не задан.",
                            isError: true);
                    }
                }
                catch (InvalidOperationException ex)
                {
                    SetProjectStatus($"Ошибка перехода из условия «{scene.Name}»: {ex.Message}", isError: true);
                }
            }
        }

        private void ContinueAfterAction(SceneModel scene, HashSet<string> visited)
        {
            if (!_isEngineRunning)
                return;

            try
            {
                var next = FindReferencedScene(scene.TargetSceneId, scene.TargetSceneName);
                if (next is not null)
                    NavigateToScene(next, visited);
                else
                    SetProjectStatus($"Действие «{scene.Name}» не имеет следующего перехода.", isError: false);
            }
            catch (InvalidOperationException ex)
            {
                SetProjectStatus($"Ошибка перехода из действия «{scene.Name}»: {ex.Message}", isError: true);
            }
        }

        private void ExecuteSystemCommand(string? systemCommand)
        {
            if (!_isEngineRunning || string.IsNullOrWhiteSpace(systemCommand)) return;

            var parts = systemCommand.Split(':', 2);
            string command = parts[0].Trim().ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                switch (command)
                {
                    case "playsound":
                        if (!string.IsNullOrEmpty(arg))
                        {
                            string soundPath = ResolveAssetPath(arg);
                            QueueRuntimeAction(() => Engine.PlaySound(soundPath));
                        }
                        break;

                    case "playmusic":
                        if (!string.IsNullOrEmpty(arg))
                        {
                            string musicPath = ResolveAssetPath(arg);
                            QueueRuntimeAction(() => Engine.PlayMusic(musicPath));
                        }
                        break;

                    case "stopmusic":
                        QueueRuntimeAction(Engine.StopMusic);
                        break;

                    case "stopsound":
                        QueueRuntimeAction(Engine.StopSounds);
                        break;

                    case "volume":
                        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float vol))
                            QueueRuntimeAction(() => Engine.SetMusicVolume(vol));
                        break;

                    case "fadeout":
                        float duration = float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 2f;
                        QueueRuntimeAction(() => Engine.StartMusicFadeOut(duration));
                        break;

                    case "fontsize":
                        if (int.TryParse(arg, out int size)) QueueRuntimeAction(() => Engine.SetFontSize(size));
                        break;
                    case "setvar":
                        ProcessSetVar(arg);
                        break;
                    case "shake":
                        if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float shakeDuration))
                        {
                            QueueRuntimeAction(() => Engine.ShakeScreen(shakeDuration, 15f));
                            Console.WriteLine($"[VFX] Тряска экрана на {shakeDuration} сек.");
                        }
                        break;

                    case "flash":
                        string color = string.IsNullOrWhiteSpace(arg) ? "#FFFFFF" : arg;
                        QueueRuntimeAction(() => Engine.FlashScreen(color, 0.5f));
                        Console.WriteLine($"[VFX] Вспышка цветом {color}");
                        break;
                    default:
                        SetProjectStatus($"Неизвестная системная команда: '{systemCommand}'.", isError: true);
                        break;

                }
            }
            catch (Exception ex)
            {
                SetProjectStatus($"Ошибка выполнения системной команды «{systemCommand}»: {ex.Message}", isError: true);
            }
        }

        private void ProcessSetVar(string expression)
        {
            try
            {
                StoryRuntimeLogic.ApplyVariableCommand(expression, GameVariables);
                SetProjectStatus($"Выполнено SetVar: {expression}", isError: false);
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                SetProjectStatus($"Ошибка команды SetVar «{expression}»: {ex.Message}", isError: true);
            }
        }

        private async Task RunLuaScriptAsync(string source, Action onSuccess)
        {
            string projectPath = GetProjectAssetDirectory();
            var runner = new LuaScriptRunner(
                GameVariables,
                path => QueueLuaAudioCommand(() => Engine.PlayMusic(ResolveLuaAssetPath(projectPath, path))),
                path => QueueLuaAudioCommand(() => Engine.PlaySound(ResolveLuaAssetPath(projectPath, path))),
                () => QueueRuntimeAction(Engine.StopMusic),
                () => QueueRuntimeAction(Engine.StopSounds),
                volume => QueueRuntimeAction(() => Engine.SetMusicVolume(volume)),
                duration => QueueRuntimeAction(() => Engine.StartMusicFadeOut(duration)),
                size => QueueRuntimeAction(() => Engine.SetFontSize(size)),
                duration => QueueRuntimeAction(() => Engine.ShakeScreen(duration, 15f)),
                color => QueueRuntimeAction(() => Engine.FlashScreen(color, 0.5f)));

            try
            {
                await Task.Run(() => runner.Execute(source));
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_isEngineRunning)
                        onSuccess();
                });
            }

            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    SetProjectStatus($"Ошибка Lua: {ex.Message}", isError: true));
            }
        }

        private void QueueLuaAudioCommand(Action play)
        {
            QueueRuntimeAction(() =>
            {
                try
                {
                    play();
                }
                catch (Exception ex)
                {
                    SetProjectStatus($"Ошибка аудиокоманды Lua: {ex.Message}", isError: true);
                }
            });
        }

        private async void OnBrowseAssetClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string assetType } || ScenesListBox.SelectedItem is not SceneModel scene)
                return;
            if (_isPlayerMode)
                return;
            if (_currentProjectPath is null)
            {
                SetProjectStatus("Сначала сохраните проект, затем импортируйте в него ресурсы.", isError: true);
                return;
            }

            try
            {
                IReadOnlyList<FilePickerFileType> filters = assetType switch
                {
                    "Background" or "CharacterSprite" => new[]
                    {
                        new FilePickerFileType("Изображения") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
                    },
                    "Music" or "Sound" => new[]
                    {
                        new FilePickerFileType("Аудио") { Patterns = new[] { "*.ogg", "*.wav", "*.flac" } }
                    },
                    _ => throw new InvalidOperationException($"Неизвестный тип ресурса «{assetType}».")
                };

                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Импортировать ресурс в проект",
                    AllowMultiple = false,
                    FileTypeFilter = filters
                });
                if (files.Count == 0)
                    return;

                string projectDirectory = GetProjectAssetDirectory();
                string relativePath = await Task.Run(() =>
                    ProjectAssetService.Import(files[0].Path.LocalPath, projectDirectory));
                switch (assetType)
                {
                    case "Background":
                        scene.Background = relativePath;
                        break;
                    case "CharacterSprite":
                        scene.CharacterSprite = relativePath;
                        break;
                    case "Music":
                        scene.Music = relativePath;
                        break;
                    case "Sound":
                        scene.SystemCommand = $"PlaySound: {relativePath}";
                        break;
                }
                RefreshProjectAssets();
                SetProjectStatus($"Ресурс импортирован: {relativePath}", isError: false);
            }
            catch (Exception ex)
            {
                SetProjectStatus($"Не удалось импортировать ресурс: {ex.Message}", isError: true);
            }
        }

        private async void OnSaveProjectClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var errors = StoryProjectValidator.Validate(Scenes, GetProjectAssetDirectory())
                    .Where(diagnostic => diagnostic.Severity == StoryDiagnosticSeverity.Error)
                    .Select(diagnostic => diagnostic.Message)
                    .ToArray();
                if (errors.Length > 0)
                {
                    SetProjectStatus($"Сначала исправьте ошибки проекта:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}", isError: true);
                    return;
                }

                var json = ProjectFileService.Save(Scenes);
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Сохранить проект новеллы",
                    SuggestedFileName = _currentProjectPath is null ? "project.novel" : Path.GetFileName(_currentProjectPath),
                    DefaultExtension = "novel",
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("Novella project") { Patterns = new[] { "*.novel" } }
                    }
                });
                if (file is null) return;

                await using var stream = await file.OpenWriteAsync();
                if (stream.CanSeek)
                {
                    stream.SetLength(0);
                    stream.Position = 0;
                }
                var data = Encoding.UTF8.GetBytes(json);
                await stream.WriteAsync(data);
                _currentProjectPath = file.Path.LocalPath;
                RefreshProjectAssets();
                SetProjectStatus($"Сохранено: {_currentProjectPath}", isError: false);
            }
            catch (Exception ex)
            {
                SetProjectStatus($"Ошибка сохранения проекта: {ex.Message}", isError: true);
            }
        }

        private async void OnLoadProjectClick(object? sender, RoutedEventArgs e)
        {
            try
            {
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Открыть проект новеллы",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Novella project") { Patterns = new[] { "*.novel" } }
                    }
                });
                if (files.Count == 0) return;

                var file = files[0];
                await using var stream = await file.OpenReadAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var loadedScenes = ProjectFileService.Load(await reader.ReadToEndAsync());

                _undoHistory.Clear();
                UpdateUndoButtons();
                ScenesListBox.SelectedItem = null;
                SceneSearchBox.Text = "";
                Scenes.Clear();
                foreach (var scene in loadedScenes) Scenes.Add(scene);
                RefreshFilteredScenes();
                RefreshReferenceNames();
                _currentProjectPath = file.Path.LocalPath;
                RefreshProjectAssets();
                SetProjectStatus($"Открыт: {_currentProjectPath}", isError: false);
                UpdateConnectionLine();
            }
            catch (Exception ex)
            {
                SetProjectStatus($"Ошибка открытия проекта: {ex.Message}", isError: true);
            }
        }

        private void OnDeleteSceneClick(object? sender, RoutedEventArgs e)
        {
            if (ScenesListBox.SelectedItem is SceneModel selected && Scenes.Count > 1)
            {
                string before = CaptureSceneSnapshot();
                int index = Scenes.IndexOf(selected);
                Scenes.Remove(selected);
                RemoveReferencesToScene(selected);
                RecordUndoSnapshot(before);
                RefreshFilteredScenes();
                ScenesListBox.SelectedIndex = Math.Max(0, index - 1);
                UpdateConnectionLine();
            }
        }

        private void RemoveReferencesToScene(SceneModel removed)
        {
            foreach (var scene in Scenes)
            {
                if (scene.TargetSceneId == removed.Id)
                {
                    scene.TargetSceneId = "";
                    scene.TargetSceneName = "";
                }
                if (scene.TargetSceneIfTrueId == removed.Id)
                {
                    scene.TargetSceneIfTrueId = "";
                    scene.TargetSceneIfTrue = "";
                }
                if (scene.TargetSceneIfFalseId == removed.Id)
                {
                    scene.TargetSceneIfFalseId = "";
                    scene.TargetSceneIfFalse = "";
                }
                foreach (var choice in scene.Choices.Where(choice => choice.TargetSceneId == removed.Id))
                {
                    choice.TargetSceneId = "";
                    choice.TargetSceneName = "";
                }
            }
        }

        private void OnSceneSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedSceneDiagnostics();
            if (ScenesListBox.SelectedItem is SceneModel selected)
            {
                TriggerEngineUpdate(musicChanged: true);
            }
        }

        private void OnFontSizeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            int size = (int)e.NewValue;
            if (FontSizeTxt != null) FontSizeTxt.Text = $"{size}px";
            QueueRuntimeAction(() => Engine.SetFontSize(size));
        }

        public void OnVolumeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            float volume = (float)e.NewValue;
            if (VolumeTxt != null) VolumeTxt.Text = $"{(int)volume}%";
            QueueRuntimeAction(() => Engine.SetMusicVolume(volume));
        }

        private void OnTextRevealSpeedChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            float speed = (float)e.NewValue;
            if (TextRevealSpeedTxt is not null)
                TextRevealSpeedTxt.Text = speed <= 0 ? "Мгновенно" : $"{speed:0} знаков/с";
            QueueRuntimeAction(() => TrySetTextRevealSpeed(speed));
        }

        private void TrySetTextRevealSpeed(float speed)
        {
            if (!_nativeRevealApiSupported)
                return;

            try
            {
                Engine.SetTextRevealSpeed(speed);
            }
            catch (EntryPointNotFoundException)
            {
                _nativeRevealApiSupported = false;
                Dispatcher.UIThread.Post(() =>
                    SetProjectStatus("EngineCore.dll устарела: постепенный вывод текста недоступен. Пересоберите нативное ядро.", isError: true));
            }
        }

        public void OnFadeOutClick(object? sender, RoutedEventArgs e)
        {
            QueueRuntimeAction(() => Engine.StartMusicFadeOut(2.0f));
        }

        private void TriggerEngineUpdate(bool musicChanged = false)
        {
            if (!_isEngineRunning || ScenesListBox.SelectedItem is not SceneModel selected) return;
            SendSceneToEngine(selected, musicChanged);
        }

        private sealed record RuntimeSceneUpdate(
            string Background,
            string Text,
            string CharacterName,
            string CharacterSprite,
            float CharacterX,
            float CharacterY,
            string CharacterAnimation,
            string? Choice1,
            string? Choice2,
            string? Choice3,
            string? Choice4,
            string Music,
            string NodeType,
            bool WasSeen);

        private RuntimeSceneUpdate CreateRuntimeSceneUpdate(SceneModel scene)
        {
            if (scene.Choices.Count > 4)
                throw new InvalidOperationException($"Сцена «{scene.Name}» содержит больше 4 вариантов; preview поддерживает максимум 4.");

            string? c1 = scene.Choices.Count > 0 ? ProcessTextVariables(scene.Choices[0].Text) : null;
            string? c2 = scene.Choices.Count > 1 ? ProcessTextVariables(scene.Choices[1].Text) : null;
            string? c3 = scene.Choices.Count > 2 ? ProcessTextVariables(scene.Choices[2].Text) : null;
            string? c4 = scene.Choices.Count > 3 ? ProcessTextVariables(scene.Choices[3].Text) : null;

            return new RuntimeSceneUpdate(
                ResolveAssetPath(scene.Background),
                ProcessTextVariables(scene.Text),
                scene.CharacterName ?? "",
                ResolveAssetPath(scene.CharacterSprite),
                scene.CharacterX,
                scene.CharacterY,
                scene.CharacterAnimation ?? "None",
                c1,
                c2,
                c3,
                c4,
                ResolveAssetPath(scene.Music),
                scene.NodeType,
                _seenDialogueSceneIds.Contains(scene.Id));
        }

        private string ResolveAssetPath(string? assetPath)
        {
            return ResolveAssetPathFrom(GetProjectAssetDirectory(), assetPath);
        }

        private string GetProjectAssetDirectory()
        {
            if (_currentProjectPath is null)
                return AppDomain.CurrentDomain.BaseDirectory;

            return Path.GetDirectoryName(_currentProjectPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string ResolveAssetPathFrom(string projectPath, string? assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return "";

            if (Path.IsPathRooted(assetPath))
                return Path.GetFullPath(assetPath);

            return Path.GetFullPath(Path.Combine(projectPath, assetPath));
        }

        private static string ResolveLuaAssetPath(string projectPath, string assetPath)
        {
            if (Path.IsPathRooted(assetPath))
                throw new InvalidOperationException("Lua media paths must be relative to the project.");

            string fullProjectPath = Path.GetFullPath(projectPath);
            string resolvedPath = Path.GetFullPath(Path.Combine(fullProjectPath, assetPath));
            string relativePath = Path.GetRelativePath(fullProjectPath, resolvedPath);
            if (Path.IsPathRooted(relativePath)
                || relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Lua media paths cannot escape the project directory.");
            }

            return resolvedPath;
        }

        public void SendSceneToEngine(
            SceneModel scene,
            bool playMusic = false,
            bool recordHistory = false,
            bool? wasSeen = null)
        {
            if (scene is null || !_isEngineRunning) return;

            RuntimeSceneUpdate update = CreateRuntimeSceneUpdate(scene);
            if (wasSeen.HasValue)
                update = update with { WasSeen = wasSeen.Value };
            _runtimeActions.Enqueue(() => ApplySceneToEngine(update, playMusic, recordHistory));
        }

        private static void ApplySceneToEngine(RuntimeSceneUpdate scene, bool playMusic, bool recordHistory)
        {
            Engine.UpdateScene(
                scene.Background,
                scene.Text,
                scene.CharacterName,
                scene.CharacterSprite,
                scene.CharacterX,
                scene.CharacterY);
            Engine.SetCharacterAnimation(scene.CharacterAnimation);
            Engine.UpdateChoices(scene.Choice1, scene.Choice2, scene.Choice3, scene.Choice4);
            Engine.SetPlaybackState(scene.WasSeen, scene.NodeType == "Choice");
            if (recordHistory && scene.NodeType == "Dialogue")
                Engine.AddDialogueHistory(scene.CharacterName, scene.Text);

            if (playMusic && !string.IsNullOrEmpty(scene.Music))
                Engine.PlayMusic(scene.Music);
        }

        private void QueueRuntimeAction(Action action)
        {
            if (_isEngineRunning)
                _runtimeActions.Enqueue(action);
        }

        public void OnStartEngineClick(object? sender, RoutedEventArgs e)
        {
            if (_isEngineRunning) return;
            if (!_nativeEngineAvailable)
            {
                string message = "Не удалось запустить preview: EngineCore.dll или её зависимости отсутствуют. Соберите и разместите нативное ядро рядом с редактором.";
                if (_isPlayerMode)
                    ShowPlayerError(message);
                else
                    SetProjectStatus(message, isError: true);
                return;
            }

            SceneModel? selected = _isPlayerMode
                ? Scenes.FirstOrDefault()
                : ScenesListBox.SelectedItem as SceneModel;
            if (selected is null)
            {
                if (_isPlayerMode)
                    ShowPlayerError("Нельзя запустить игру: в проекте нет сцен.");
                return;
            }

            if (_isPlayerMode)
                ScenesListBox.SelectedItem = selected;

            var errors = StoryProjectValidator.Validate(Scenes, GetProjectAssetDirectory())
                .Where(diagnostic => diagnostic.Severity == StoryDiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Message)
                .ToArray();
            if (errors.Length > 0)
            {
                ShowPlayerError($"Нельзя запустить игру. Исправьте ошибки:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
                return;
            }

            while (_runtimeActions.TryDequeue(out _)) { }

            int initialFontSize = (int)FontSizeSlider.Value;
            float textRevealSpeed = (float)TextRevealSpeedSlider.Value;
            RuntimeSceneUpdate initialScene = CreateRuntimeSceneUpdate(selected);
            _seenDialogueSceneIds.Clear();
            bool initialSceneWasSeen = selected.NodeType == "Dialogue"
                && !_seenDialogueSceneIds.Add(selected.Id);
            if (initialSceneWasSeen)
                initialScene = initialScene with { WasSeen = true };
            _isEngineRunning = true;

            Thread engineThread = new Thread(() =>
            {
                bool runtimeFailed = false;
                try
                {
                    Engine.InitEngine(1024, 524, "Engine Runtime Core", AppDomain.CurrentDomain.BaseDirectory);
                    Engine.SetFontSize(initialFontSize);
                    TrySetTextRevealSpeed(textRevealSpeed);
                    Engine.SetGameUiState(_gameUiState);
                    ApplySceneToEngine(initialScene, playMusic: true, recordHistory: false);
                    Dispatcher.UIThread.Post(() => NavigateToScene(selected));

                    Stopwatch stopwatch = new Stopwatch();
                    stopwatch.Start();
                    float lastTime = 0f;

                    while (_isEngineRunning)
                    {
                        float currentTime = (float)stopwatch.Elapsed.TotalSeconds;
                        float deltaTime = currentTime - lastTime;
                        lastTime = currentTime;

                        while (_runtimeActions.TryDequeue(out var runtimeAction))
                        {
                            try
                            {
                                runtimeAction();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Ошибка команды Runtime: {ex.Message}");
                                Dispatcher.UIThread.Post(() =>
                                    SetProjectStatus($"Ошибка команды Runtime: {ex.Message}", isError: true));
                            }
                        }

                        bool shouldContinue = Engine.TickEngine(deltaTime);
                        ReportNativeErrors();
                        if (!shouldContinue)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    runtimeFailed = true;
                    Console.WriteLine($"Ошибка Runtime: {ex.Message}");
                    Dispatcher.UIThread.Post(() => ShowPlayerError($"Ошибка Runtime: {ex.Message}"));
                }
                finally
                {
                    Engine.CloseEngine();
                    _isEngineRunning = false;
                    while (_runtimeActions.TryDequeue(out _)) { }
                    if (_isPlayerMode && !runtimeFailed)
                        Dispatcher.UIThread.Post(Close);
                }
            });

            engineThread.IsBackground = true;
            if (OperatingSystem.IsWindows()) engineThread.SetApartmentState(ApartmentState.STA);
            engineThread.Start();
        }

        private void ReportNativeErrors()
        {
            if (!_nativeErrorApiSupported)
                return;

            try
            {
                int length = Engine.GetLastErrorMessage(_nativeErrorBuffer, _nativeErrorBuffer.Length);
                if (length <= 0)
                    return;

                int availableBytes = Math.Min(length, _nativeErrorBuffer.Length - 1);
                string message = Encoding.UTF8.GetString(_nativeErrorBuffer, 0, availableBytes);
                if (length > availableBytes)
                    message += " (сообщение обрезано)";
                Dispatcher.UIThread.Post(() => ShowRuntimeDiagnostic($"Ошибка ресурсов ядра: {message}"));
            }
            catch (EntryPointNotFoundException)
            {
                _nativeErrorApiSupported = false;
                Dispatcher.UIThread.Post(() =>
                    ShowRuntimeDiagnostic("EngineCore.dll устарела и не сообщает ошибки ресурсов. Пересоберите нативное ядро из текущего проекта."));
            }
        }

        private void ShowRuntimeDiagnostic(string message)
        {
            SetProjectStatus(message, isError: true);
            if (!_isPlayerMode)
                return;

            var closeButton = new Button
            {
                Content = "Закрыть",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
            };
            var dialog = new Window
            {
                Title = "Ошибка игры",
                Width = 520,
                Height = 190,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 18,
                    Children =
                    {
                        new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                        closeButton
                    }
                }
            };
            closeButton.Click += (_, _) => dialog.Close();
            dialog.Show();
        }

        private void ShowPlayerError(string message)
        {
            SetProjectStatus(message, isError: true);
            if (!_isPlayerMode)
                return;

            Opacity = 1;
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Show();
        }

        private string ProcessTextVariables(string rawText)
        {
            return StoryRuntimeLogic.SubstituteTextVariables(rawText, GameVariables);
        }

        public void OnStopEngineClick(object? sender, RoutedEventArgs e) => _isEngineRunning = false;

        protected override void OnClosing(WindowClosingEventArgs e)
        {
            SaveWorkspaceSettings();
            StopAssetAudio();
            DisposeAssetPreviewBitmap();
            _isEngineRunning = false;
            base.OnClosing(e);
        }
    }
}