using System;
using System.Collections.ObjectModel;

namespace EngineEditor.Models
{
    public class SceneModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString(); // Уникальный ID нода
        public string Name { get; set; } = "Диалог";
        public string CharacterName { get; set; } = "Персонаж";
        public string Text { get; set; } = "Текст реплики...";

        // НОВОЕ: Цвет шапки нода для визуального разделения (Диалог, Выбор, Действие)
        public string HeaderColor { get; set; } = "#007acc";

        // Параметры для рендеринга в C++ движке
        public string Background { get; set; } = "";
        public string CharacterSprite { get; set; } = "";
        public float CharacterX { get; set; } = 50f;
        public float CharacterY { get; set; } = 100f;
        public string Music { get; set; } = "";

        // Координаты нода на графическом холсте редактора
        public double X { get; set; } = 200;
        public double Y { get; set; } = 200;
        // Тип нода: "Dialogue", "Choice" или "Action"
        public string NodeType { get; set; } = "Dialogue";

        // Имя следующей сцены для прямого перехода (без вариантов выбора)
        public string TargetSceneName { get; set; } = "";

        // Команда или скрипт для системного нода (например: "PlaySound: jump.wav")
        public string SystemCommand { get; set; } = "";
        // Список вариантов выбора (переходов к другим нодам)
        public ObservableCollection<ChoiceModel> Choices { get; set; } = new ObservableCollection<ChoiceModel>();
    }
}