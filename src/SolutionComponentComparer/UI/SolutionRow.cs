using System;
using System.ComponentModel;
using SolutionComponentComparer.Core;
using SolutionComponentComparer.Data;

namespace SolutionComponentComparer.UI
{
    /// <summary>A solution in the picker grid. Source and Target are mutually exclusive.</summary>
    public class SolutionRow : INotifyPropertyChanged
    {
        private bool _source;
        private bool _target;

        public SolutionRow(SolutionInfo info)
        {
            Info = info;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [Browsable(false)]
        public SolutionInfo Info { get; private set; }

        public bool Source
        {
            get { return _source; }
            set
            {
                if (_source == value) return;
                _source = value;
                if (value && _target)
                {
                    _target = false;
                    OnChanged("Target");
                }
                OnChanged("Source");
            }
        }

        public bool Target
        {
            get { return _target; }
            set
            {
                if (_target == value) return;
                _target = value;
                if (value && _source)
                {
                    _source = false;
                    OnChanged("Source");
                }
                OnChanged("Target");
            }
        }

        public string DisplayName { get { return Info.FriendlyName; } }
        public string UniqueName { get { return Info.UniqueName; } }
        public string Version { get { return Info.Version; } }
        public bool Managed { get { return Info.IsManaged; } }
        public string Publisher { get { return Info.Publisher; } }
        public DateTime? InstalledOn { get { return Info.InstalledOn; } }

        [Browsable(false)]
        public SolutionRole Role
        {
            get { return _source ? SolutionRole.Source : _target ? SolutionRole.Target : SolutionRole.None; }
        }

        public SolutionRef ToRef()
        {
            return new SolutionRef
            {
                Id = Info.Id,
                UniqueName = Info.UniqueName,
                FriendlyName = Info.FriendlyName,
                Version = Info.Version,
                IsManaged = Info.IsManaged,
                Role = Role
            };
        }

        private void OnChanged(string property)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }
    }
}
