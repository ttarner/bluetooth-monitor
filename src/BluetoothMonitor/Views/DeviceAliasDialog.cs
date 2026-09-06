using System.Windows;

namespace BluetoothMonitor.Views;

public sealed class DeviceAliasDialog : Window
{
    private readonly System.Windows.Controls.TextBox _aliasTextBox;

    public string Alias => _aliasTextBox.Text.Trim();

    public DeviceAliasDialog(string deviceName, string currentAlias)
    {
        Title = "Device Alias";
        Width = 380;
        Height = 266;
        MinWidth = 380;
        MinHeight = 266;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;
        Background = System.Windows.Media.Brushes.Transparent;
        Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EAF2FF"));
        ShowInTaskbar = false;
        UseLayoutRounding = true;

        var panelBackgroundBrush = new System.Windows.Media.LinearGradientBrush();
        panelBackgroundBrush.StartPoint = new System.Windows.Point(0, 0);
        panelBackgroundBrush.EndPoint = new System.Windows.Point(1, 1);
        panelBackgroundBrush.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#172131"), 0));
        panelBackgroundBrush.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#111A28"), 1));

        var windowBackgroundBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0B1422"));
        var titleBarBrush = new System.Windows.Media.LinearGradientBrush();
        titleBarBrush.StartPoint = new System.Windows.Point(0, 0);
        titleBarBrush.EndPoint = new System.Windows.Point(1, 0);
        titleBarBrush.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0E1825"), 0));
        titleBarBrush.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#132234"), 0.5));
        titleBarBrush.GradientStops.Add(new System.Windows.Media.GradientStop((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#101A28"), 1));

        var titleBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#EAF2FF"));
        var mutedBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8FA5BA"));
        var textBoxBackgroundBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#101A27"));
        var textBoxBorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#304458"));
        var accentBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6EE7D8"));
        var windowBorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1C2838"));

        var outerBorder = new System.Windows.Controls.Border
        {
            Background = windowBackgroundBrush,
            BorderBrush = windowBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            SnapsToDevicePixels = true
        };

        var windowGrid = new System.Windows.Controls.Grid();
        windowGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        windowGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new System.Windows.Controls.Border
        {
            Background = titleBarBrush,
            BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#223245")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(12, 12, 0, 0),
            Height = 42
        };
        titleBar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                return;
            }

            DragMove();
        };

        var titleBarGrid = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(14, 0, 8, 0)
        };
        titleBarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBarGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var titleBarText = new System.Windows.Controls.TextBlock
        {
            Text = "Device Alias",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = titleBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(titleBarText, 0);
        titleBarGrid.Children.Add(titleBarText);

        var closeButton = new System.Windows.Controls.Button
        {
            Content = "✕",
            Width = 32,
            Height = 30,
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#141F2D")),
            Foreground = titleBrush,
            BorderBrush = textBoxBorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center
        };
        closeButton.Click += (_, _) => Close();
        System.Windows.Controls.Grid.SetColumn(closeButton, 1);
        titleBarGrid.Children.Add(closeButton);

        titleBar.Child = titleBarGrid;
        System.Windows.Controls.Grid.SetRow(titleBar, 0);
        windowGrid.Children.Add(titleBar);

        var card = new System.Windows.Controls.Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(18),
            Background = panelBackgroundBrush,
            BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#223245")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18)
        };

        var root = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0)
        };
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

        var title = new System.Windows.Controls.TextBlock
        {
            Text = "Set alias",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = titleBrush
        };
        System.Windows.Controls.Grid.SetRow(title, 0);
        root.Children.Add(title);

        var description = new System.Windows.Controls.TextBlock
        {
            Text = $"Overlay name for {deviceName}",
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = mutedBrush,
            TextWrapping = TextWrapping.Wrap
        };
        System.Windows.Controls.Grid.SetRow(description, 1);
        root.Children.Add(description);

        _aliasTextBox = new System.Windows.Controls.TextBox
        {
            Text = currentAlias,
            Margin = new Thickness(0, 14, 0, 0),
            Height = 38,
            VerticalContentAlignment = VerticalAlignment.Center,
            MaxLength = 40,
            Background = textBoxBackgroundBrush,
            Foreground = titleBrush,
            BorderBrush = textBoxBorderBrush,
            CaretBrush = accentBrush,
            SelectionBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#176179")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 0, 12, 0)
        };
        System.Windows.Controls.Grid.SetRow(_aliasTextBox, 2);
        root.Children.Add(_aliasTextBox);

        var buttons = new System.Windows.Controls.Grid
        {
            Margin = new Thickness(0, 16, 0, 0)
        };
        buttons.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        buttons.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var clearButton = new System.Windows.Controls.Button
        {
            Content = "Clear",
            MinWidth = 88,
            Height = 40,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#18293B")),
            Foreground = mutedBrush,
            BorderBrush = textBoxBorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center
        };
        clearButton.Click += (_, _) => _aliasTextBox.Text = string.Empty;
        System.Windows.Controls.Grid.SetColumn(clearButton, 0);
        buttons.Children.Add(clearButton);

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            MinWidth = 88,
            Height = 40,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#182435")),
            Foreground = titleBrush,
            BorderBrush = textBoxBorderBrush,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center
        };
        System.Windows.Controls.Grid.SetColumn(cancelButton, 1);
        buttons.Children.Add(cancelButton);

        var saveButton = new System.Windows.Controls.Button
        {
            Content = "Save",
            MinWidth = 88,
            Height = 40,
            IsDefault = true,
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#114257")),
            Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#E7F9FF")),
            BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2FD0EC")),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center
        };
        saveButton.Click += (_, _) => DialogResult = true;
        System.Windows.Controls.Grid.SetColumn(saveButton, 2);
        buttons.Children.Add(saveButton);

        System.Windows.Controls.Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        card.Child = root;
        System.Windows.Controls.Grid.SetRow(card, 1);
        windowGrid.Children.Add(card);

        outerBorder.Child = windowGrid;
        Content = outerBorder;
        Loaded += (_, _) =>
        {
            _aliasTextBox.Focus();
            _aliasTextBox.SelectAll();
        };
    }
}
