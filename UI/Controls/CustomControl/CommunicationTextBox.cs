using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace UI.Controls.CustomControl
{
    public interface ICommunicationReader
    {
        T? Read<T>(string address);
        void Write(string address, object value);
    }

    public class CommunicationTextBox : TextBox, IDisposable
    {
        private bool _isDisposed = false;
        private bool _isEditing = false;
        private object? _currentValue = null;

        #region Dependency Properties

        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register(nameof(Address), typeof(string), typeof(CommunicationTextBox),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty ConnectionNameProperty =
            DependencyProperty.Register(nameof(ConnectionName), typeof(string), typeof(CommunicationTextBox),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty DataTypeProperty =
            DependencyProperty.Register(nameof(DataType), typeof(Type), typeof(CommunicationTextBox),
                new PropertyMetadata(typeof(short)));

        public static readonly DependencyProperty IsReadOnlyWhenWritingProperty =
            DependencyProperty.Register(nameof(IsReadOnlyWhenWriting), typeof(bool), typeof(CommunicationTextBox),
                new PropertyMetadata(true));

        public static readonly DependencyProperty WriteOnEnterProperty =
            DependencyProperty.Register(nameof(WriteOnEnter), typeof(bool), typeof(CommunicationTextBox),
                new PropertyMetadata(true));

        public static readonly DependencyProperty WriteOnLostFocusProperty =
            DependencyProperty.Register(nameof(WriteOnLostFocus), typeof(bool), typeof(CommunicationTextBox),
                new PropertyMetadata(true));

        public static readonly DependencyProperty CommunicationReaderProperty =
            DependencyProperty.Register(nameof(CommunicationReader), typeof(ICommunicationReader), typeof(CommunicationTextBox),
                new PropertyMetadata(null, OnReaderChanged));

        public static readonly DependencyProperty AutoRefreshProperty =
            DependencyProperty.Register(nameof(AutoRefresh), typeof(bool), typeof(CommunicationTextBox),
                new PropertyMetadata(false, OnAutoRefreshChanged));

        public static readonly DependencyProperty RefreshIntervalMsProperty =
            DependencyProperty.Register(nameof(RefreshIntervalMs), typeof(int), typeof(CommunicationTextBox),
                new PropertyMetadata(1000));

        #endregion

        #region Public Properties

        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }

        public string ConnectionName
        {
            get => (string)GetValue(ConnectionNameProperty);
            set => SetValue(ConnectionNameProperty, value);
        }

        public Type DataType
        {
            get => (Type)GetValue(DataTypeProperty);
            set => SetValue(DataTypeProperty, value);
        }

        public bool IsReadOnlyWhenWriting
        {
            get => (bool)GetValue(IsReadOnlyWhenWritingProperty);
            set => SetValue(IsReadOnlyWhenWritingProperty, value);
        }

        public bool WriteOnEnter
        {
            get => (bool)GetValue(WriteOnEnterProperty);
            set => SetValue(WriteOnEnterProperty, value);
        }

        public bool WriteOnLostFocus
        {
            get => (bool)GetValue(WriteOnLostFocusProperty);
            set => SetValue(WriteOnLostFocusProperty, value);
        }

        public ICommunicationReader? CommunicationReader
        {
            get => (ICommunicationReader?)GetValue(CommunicationReaderProperty);
            set => SetValue(CommunicationReaderProperty, value);
        }

        public bool AutoRefresh
        {
            get => (bool)GetValue(AutoRefreshProperty);
            set => SetValue(AutoRefreshProperty, value);
        }

        public int RefreshIntervalMs
        {
            get => (int)GetValue(RefreshIntervalMsProperty);
            set => SetValue(RefreshIntervalMsProperty, value);
        }

        #endregion

        public CommunicationTextBox()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            GotKeyboardFocus += OnGotKeyboardFocus;
            LostKeyboardFocus += OnLostKeyboardFocus;
            PreviewKeyDown += OnPreviewKeyDown;
        }

        #region Event Handlers

        private static void OnReaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CommunicationTextBox ctb)
            {
                ctb.ReadValue();
            }
        }

        private static void OnAutoRefreshChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CommunicationTextBox ctb)
            {
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            ReadValue();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
        }

        private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _isEditing = true;
        }

        private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _isEditing = false;
            if (WriteOnLostFocus)
            {
                CommitWrite();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && WriteOnEnter)
            {
                CommitWrite();
                e.Handled = true;
                MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
            else if (e.Key == Key.Escape)
            {
                RestoreValue();
                e.Handled = true;
            }
        }

        #endregion

        #region Private Methods

        private void ReadValue()
        {
            if (CommunicationReader == null || string.IsNullOrWhiteSpace(Address))
                return;

            try
            {
                var value = ReadByType(CommunicationReader, Address, DataType);
                if (value != null)
                {
                    _currentValue = value;
                    if (!_isEditing)
                    {
                        Text = FormatValue(value);
                    }
                }
            }
            catch
            {
                Text = "Error";
            }
        }

        private void CommitWrite()
        {
            if (CommunicationReader == null || string.IsNullOrWhiteSpace(Address))
                return;

            if (IsReadOnlyWhenWriting)
            {
                IsEnabled = false;
                IsReadOnly = true;
            }

            try
            {
                var value = ParseText(Text, DataType);
                if (value != null)
                {
                    CommunicationReader.Write(Address, value);
                    _currentValue = value;
                    Text = FormatValue(value);
                }
            }
            catch
            {
                RestoreValue();
            }
            finally
            {
                if (IsReadOnlyWhenWriting)
                {
                    IsEnabled = true;
                    IsReadOnly = false;
                }
            }
        }

        private void RestoreValue()
        {
            if (_currentValue != null)
            {
                Text = FormatValue(_currentValue);
            }
        }

        private object? ReadByType(ICommunicationReader reader, string address, Type type)
        {
            if (type == typeof(bool))
                return reader.Read<bool>(address);
            if (type == typeof(short))
                return reader.Read<short>(address);
            if (type == typeof(int))
                return reader.Read<int>(address);
            if (type == typeof(float))
                return reader.Read<float>(address);
            if (type == typeof(double))
                return reader.Read<double>(address);
            if (type == typeof(byte))
                return reader.Read<byte>(address);
            if (type == typeof(ushort))
                return reader.Read<ushort>(address);
            if (type == typeof(uint))
                return reader.Read<uint>(address);

            return reader.Read<object>(address);
        }

        private object? ParseText(string text, Type type)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                if (type == typeof(bool))
                    return bool.Parse(text);
                if (type == typeof(short))
                    return short.Parse(text);
                if (type == typeof(int))
                    return int.Parse(text);
                if (type == typeof(float))
                    return float.Parse(text);
                if (type == typeof(double))
                    return double.Parse(text);
                if (type == typeof(byte))
                    return byte.Parse(text);
                if (type == typeof(ushort))
                    return ushort.Parse(text);
                if (type == typeof(uint))
                    return uint.Parse(text);

                return text;
            }
            catch
            {
                return null;
            }
        }

        private string FormatValue(object? value)
        {
            if (value == null) return string.Empty;

            if (value is float f)
                return f.ToString("F2");
            if (value is double d)
                return d.ToString("F2");
            if (value is bool b)
                return b ? "1" : "0";

            return value.ToString() ?? string.Empty;
        }

        #endregion

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
    }
}
