using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EngineEditor.Models
{
    public class SceneModel : INotifyPropertyChanged
    {
        private string _name = "Новая сцена";
        private string _background = "bg1.jpg";
        private string _text = "Введите текст диалога...";
        private string _music = "";

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Background
        {
            get => _background;
            set { _background = value; OnPropertyChanged(); }
        }

        public string Text
        {
            get => _text;
            set { _text = value; OnPropertyChanged(); }
        }

        public string Music
        {
            get => _music;
            set { _music = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}