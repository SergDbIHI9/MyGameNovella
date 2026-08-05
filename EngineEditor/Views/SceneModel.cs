using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EngineEditor.Models
{
    public class SceneModel : INotifyPropertyChanged
    {
        private string _name = "Диалог";
        private string _characterAnimation = "None";
        private string _characterName = "Персонаж";
        private string _text = "Текст реплики...";
        private string _headerColor = "#007acc";
        private string _background = "";
        private string _characterSprite = "";
        private float _characterX = 50f;
        private float _characterY = 100f;
        private string _music = "";
        private double _x = 200;
        private double _y = 200;
        private string _nodeType = "Dialogue";
        private string _targetSceneName = "";
        private string _systemCommand = "";

        public string Id { get; set; } = Guid.NewGuid().ToString(); // Уникальный ID нода

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }


// Тип анимации появления (None, FadeIn, SlideLeft, SlideRight, Bounce)
public string CharacterAnimation
{
    get => _characterAnimation;
    set { _characterAnimation = value; OnPropertyChanged(); }
}
        public string CharacterName
        {
            get => _characterName;
            set { _characterName = value; OnPropertyChanged(); }
        }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        // Цвет шапки нода для визуального разделения (Диалог, Выбор, Действие)
        public string HeaderColor
        {
            get => _headerColor;
            set { _headerColor = value; OnPropertyChanged(); }
        }
        // --- ПОЛЯ ДЛЯ УЗЛА УСЛОВИЯ (CONDITION) ---

        // Название переменной (например, "Money")
        public string ConditionVariable { get; set; } = "";

        // Оператор сравнения (==, >, <, >=, <=, !=)
        public string ConditionOperator { get; set; } = "==";

        // Значение для сравнения (например, "100")
        public string ConditionValue { get; set; } = "";

        // Куда идти, если условие ВЫПОЛНИЛОСЬ
        public string TargetSceneIfTrue { get; set; } = "";

        // Куда идти, если условие НЕ ВЫПОЛНИЛОСЬ
        public string TargetSceneIfFalse { get; set; } = "";
        // Параметры для рендеринга в C++ движке
        public string Background
        {
            get => _background;
            set { _background = value; OnPropertyChanged(); }
        }

        public string CharacterSprite
        {
            get => _characterSprite;
            set { _characterSprite = value; OnPropertyChanged(); }
        }

        public float CharacterX
        {
            get => _characterX;
            set { _characterX = value; OnPropertyChanged(); }
        }

        public float CharacterY
        {
            get => _characterY;
            set { _characterY = value; OnPropertyChanged(); }
        }

        public string Music
        {
            get => _music;
            set { _music = value; OnPropertyChanged(); }
        }

        // Координаты нода на графическом холсте редактора
        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        // Тип нода: "Dialogue", "Choice" или "Action"
        public string NodeType
        {
            get => _nodeType;
            set { _nodeType = value; OnPropertyChanged(); }
        }

        // Имя следующей сцены для прямого перехода (Диалог/Действие, без кнопок выбора)
        public string TargetSceneName
        {
            get => _targetSceneName;
            set { _targetSceneName = value; OnPropertyChanged(); }
        }

        // Команда/скрипт для системного нода, например "PlaySound: jump.wav"
        public string SystemCommand
        {
            get => _systemCommand;
            set { _systemCommand = value; OnPropertyChanged(); }
        }

        // Список вариантов выбора (переходов к другим нодам)
        public ObservableCollection<ChoiceModel> Choices { get; set; } = new ObservableCollection<ChoiceModel>();

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}