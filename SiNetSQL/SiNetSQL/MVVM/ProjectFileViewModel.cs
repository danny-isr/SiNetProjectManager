using System.ComponentModel;
using SiNetSQL.Models;

namespace SiNetSQL.MVVM
{
    public class ProjectFileViewModel : INotifyPropertyChanged
    {
        private readonly ProjectFile _model;

        public ProjectFileViewModel(ProjectFile model)
        {
            _model = model;
        }

        public ProjectFile Model => _model;

        public string? Title
        {
            get => _model.Title;
            set
            {
                if (_model.Title != value)
                {
                    _model.Title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public float? Number
        {
            get => _model.Number;
            set
            {
                // Number is nullable float in model
                if (_model.Number != value)
                {
                    _model.Number = value;
                    OnPropertyChanged(nameof(Number));
                }
            }
        }

        public string? Typefile
        {
            get => _model.Typefile;
            set
            {
                if (_model.Typefile != value)
                {
                    _model.Typefile = value;
                    OnPropertyChanged(nameof(Typefile));
                }
            }
        }

        public bool? LookAtDes
        {
            get => _model.LookAtDes;
            set
            {
                if (_model.LookAtDes != value)
                {
                    _model.LookAtDes = value;
                    OnPropertyChanged(nameof(LookAtDes));
                }
            }
        }

        public bool? OutSidData
        {
            get => _model.OutSidData;
            set
            {
                if (_model.OutSidData != value)
                {
                    _model.OutSidData = value;
                    OnPropertyChanged(nameof(OutSidData));
                }
            }
        }

        public string? TemplateLocation
        {
            get => _model.TemplateLocation;
            set
            {
                if (_model.TemplateLocation != value)
                {
                    _model.TemplateLocation = value;
                    OnPropertyChanged(nameof(TemplateLocation));
                }
            }
        }
        
        public string? Des
        {
            get => _model.Des;
            set
            {
                if (_model.Des != value)
                {
                    _model.Des = value;
                    OnPropertyChanged(nameof(Des));
                }
            }
        }

        public int? TypeProjId
        {
            get => _model.TypeProjId;
            set
            {
                if (_model.TypeProjId != value)
                {
                    _model.TypeProjId = value;
                    OnPropertyChanged(nameof(TypeProjId));
                }
            }
        }
        
        public int? Folderid
        {
            get => _model.Folderid;
            set 
            {
                 if (_model.Folderid != value)
                 {
                     _model.Folderid = value;
                     OnPropertyChanged(nameof(Folderid));
                 }
            }
        }

        // Helper to expose Folder for moving logic if needed, 
        // passing through to Model but we might not need INPC for this 
        // if UI doesn't bind to Folder properties of the File directly for display that changes.
        public ProjectFolder? Folder
        {
            get => _model.Folder;
            set => _model.Folder = value;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
