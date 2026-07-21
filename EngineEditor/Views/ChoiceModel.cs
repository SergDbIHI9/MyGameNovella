using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EngineEditor.Models
{
    public class ChoiceModel : INotifyPropertyChanged
    {
        private string _text = "Новый выбор";
        private string _targetSceneName = "";

        // Текст, который увидит игрок на кнопке
        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        // Имя сцены, на которую ведет этот выбор
        public string TargetSceneName
        {
            get => _targetSceneName;
            set { _targetSceneName = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}