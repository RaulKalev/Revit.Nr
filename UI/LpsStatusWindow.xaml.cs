using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Renumber.UI
{
    public partial class LpsStatusWindow : Window
    {
        public sealed class StatusRow : INotifyPropertyChanged
        {
            private string _name;
            private string _value;

            public string Name
            {
                get => _name;
                set { if (_name != value) { _name = value; OnPropertyChanged(); } }
            }

            public string Value
            {
                get => _value;
                set { if (_value != value) { _value = value; OnPropertyChanged(); } }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string n = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        private readonly ObservableCollection<StatusRow> _rows = new ObservableCollection<StatusRow>();

        public LpsStatusWindow()
        {
            InitializeComponent();
            ParamRowsControl.ItemsSource = _rows;
        }

        /// <summary>Position the status window just to the right of the given window bounds.</summary>
        public void PositionNear(double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight)
        {
            // Width is explicit (240); height is SizeToContent so force layout first
            UpdateLayout();
            Left = ownerLeft + (ownerWidth  - Width)        / 2;
            Top  = ownerTop  + (ownerHeight - ActualHeight) / 2;
        }

        /// <summary>
        /// Update the displayed rows and pick counter.
        /// Safe to call from the Revit/WPF main thread.
        /// </summary>
        public void UpdateStatus(IEnumerable<(string name, string value)> paramValues, int pickCount)
        {
            PickCountText.Text = pickCount.ToString();

            var list = new List<(string, string)>(paramValues);

            // Grow or shrink row collection to match
            while (_rows.Count < list.Count)
                _rows.Add(new StatusRow());
            while (_rows.Count > list.Count)
                _rows.RemoveAt(_rows.Count - 1);

            for (int i = 0; i < list.Count; i++)
            {
                _rows[i].Name  = list[i].Item1;
                _rows[i].Value = list[i].Item2;
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        /// <summary>
        /// Set this from inside Execute() to receive nudge deltas (+1 or -1) when the user
        /// clicks the ▲/▼ buttons during an active pick session.
        /// </summary>
        public Action<int> NudgeRequested;

        private void NudgeUp_Click(object sender, RoutedEventArgs e)   => NudgeRequested?.Invoke(+1);
        private void NudgeDown_Click(object sender, RoutedEventArgs e) => NudgeRequested?.Invoke(-1);
    }
}
