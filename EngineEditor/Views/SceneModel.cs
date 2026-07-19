using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EngineEditor.Models
{
    public class SceneModel : INotifyPropertyChanged
    {
        private string _name = "Новая сцена";
        private string _background = "";
        private string _text = "Введите текст диалога...";
        private string _music = "";
        private string _characterName = "";
        private string _characterSprite = "";
        private float _characterX = 50f;
        private float _characterY = 100f;

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

        public string CharacterName
        {
            get => _characterName;
            set { _characterName = value; OnPropertyChanged(); }
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}