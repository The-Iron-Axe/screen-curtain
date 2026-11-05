using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ScreenCurtain
{
    internal sealed class BorderEditor
    {
        internal readonly ContentDialog Dialog;
        internal readonly ToggleSwitch EnabledSwitch;
        internal readonly Slider WidthSlider;
        internal readonly NumberBox WidthNumber;
        internal readonly ComboBox StylePicker;
        internal ColorPicker Picker { get; private set; }
        private readonly HyperlinkButton reset;
        internal BorderSettings Value { get; private set; }
        private readonly Action<BorderSettings> preview;
        private readonly BorderSettings resetBorder;
        private readonly StackPanel options;
        private readonly Microsoft.UI.Xaml.Shapes.Rectangle sample;
        private readonly double scale;
        private bool syncing;
        private ContentDialogResult? testResult;

        internal BorderEditor(Zone zone, XamlRoot root, Action<BorderSettings> previewAction, Zone defaults = null, bool editingDefaults = false)
        {
            Value = BorderSettings.FromZone(zone); preview = previewAction; scale = root.RasterizationScale;
            resetBorder = BorderSettings.FromZone(defaults ?? new Zone());
            var content = new StackPanel { Spacing = 14, MaxWidth = 360, RequestedTheme = ElementTheme.Light };
            content.Children.Add(Ui.Text(editingDefaults ? "设置将用于之后框选、导入的新遮罩，点击确定后保存。" : "修改会实时预览，点击确定后保存。", 13, 0x657184));
            var enableRow = Ui.Columns(Ui.Star, Ui.Auto);
            Ui.Put(enableRow, Ui.Text("显示这个选区的边框", 14), 0);
            EnabledSwitch = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(EnabledSwitch, "显示边框");
            Ui.Put(enableRow, EnabledSwitch, 0, 1); content.Children.Add(enableRow);
            sample = new Microsoft.UI.Xaml.Shapes.Rectangle { Height = 72, Fill = Ui.Brush((uint)Appearance.ValidRgb(zone.ColorRgb)),
                Opacity = zone.OpacityPercent / 100.0, StrokeLineJoin = PenLineJoin.Round };
            content.Children.Add(new Border { Background = Ui.Brush(0xeef3f9), Padding = new Thickness(10), CornerRadius = new CornerRadius(6), Child = sample });
            options = new StackPanel { Spacing = 14 }; content.Children.Add(options);
            options.Children.Add(Ui.Text("边框粗细", 14));
            var widthRow = Ui.Columns(Ui.Star, Ui.Auto, Ui.Auto);
            WidthSlider = new Slider { Minimum = 1, Maximum = 50, StepFrequency = 1, SmallChange = 1, LargeChange = 5, MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };
            WidthNumber = new NumberBox { Minimum = 1, Maximum = 50, SmallChange = 1, LargeChange = 5, Width = 88,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WidthSlider, "边框粗细");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WidthNumber, "边框粗细像素值");
            Ui.Put(widthRow, WidthSlider, 0); Ui.Put(widthRow, WidthNumber, 0, 1); Ui.Put(widthRow, Ui.Text("px", 13), 0, 2); options.Children.Add(widthRow);
            StylePicker = new ComboBox { Header = "线条样式", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string style in BorderSettings.StyleNames) StylePicker.Items.Add(style); options.Children.Add(StylePicker);
            options.Children.Add(Ui.Text("边框颜色", 14));
            Picker = new ColorPicker { IsAlphaEnabled = false, IsAlphaTextInputVisible = false, IsMoreButtonVisible = false,
                IsColorPreviewVisible = true, IsColorSliderVisible = true, IsColorChannelTextInputVisible = false, IsHexInputVisible = true,
                HorizontalAlignment = HorizontalAlignment.Center, Width = 300, MaxWidth = 300 };
            options.Children.Add(Picker);
            content.Children.Add(Ui.Text("边框绘制在选区内，并跟随遮罩的不透明度；0% 时一同隐藏。", 12, 0x657184));
            reset = new HyperlinkButton { Content = editingDefaults ? "恢复初始设置" : "使用默认边框", Padding = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += OnResetClick; content.Children.Add(reset);
            var scroll = new ScrollViewer { Content = content, Padding = new Thickness(0, 0, 12, 0), MaxHeight = Math.Max(220, root.Size.Height - 180),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
            Dialog = new ContentDialog { Title = editingDefaults ? "新建遮罩 · 默认边框" : zone.DisplayName + " · 边框设置", Content = scroll, PrimaryButtonText = "确定", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = root, RequestedTheme = ElementTheme.Light };
            EnabledSwitch.Toggled += OnEnabledChanged; WidthSlider.ValueChanged += OnSliderChanged;
            WidthNumber.ValueChanged += OnNumberChanged; StylePicker.SelectionChanged += OnStyleChanged;
            Picker.ColorChanged += OnColorChanged;
            Sync();
        }
        private void OnEnabledChanged(object sender, RoutedEventArgs e) { Changed(); }
        private void OnStyleChanged(object sender, SelectionChangedEventArgs e) { Changed(); }
        private void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!syncing) SetValues(new BorderSettings(Value.Enabled, (int)Math.Round(e.NewValue), Value.ColorRgb, Value.Style)); }
        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) { if (!syncing && double.IsFinite(e.NewValue)) SetValues(new BorderSettings(Value.Enabled, (int)Math.Round(e.NewValue), Value.ColorRgb, Value.Style)); }
        private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs e) { if (!syncing) SetValues(new BorderSettings(Value.Enabled, Value.Width, (e.NewColor.R << 16) | (e.NewColor.G << 8) | e.NewColor.B, Value.Style)); }
        private void OnResetClick(object sender, RoutedEventArgs e) { ResetValues(); }
        internal void Release()
        {
            EnabledSwitch.Toggled -= OnEnabledChanged; WidthSlider.ValueChanged -= OnSliderChanged; WidthNumber.ValueChanged -= OnNumberChanged;
            StylePicker.SelectionChanged -= OnStyleChanged; Picker.ColorChanged -= OnColorChanged;
            reset.Click -= OnResetClick;
            Dialog.Content = null;
            Picker = null;
        }
        private void Changed()
        {
            if (!syncing) SetValues(new BorderSettings(EnabledSwitch.IsOn, Value.Width, Value.ColorRgb, StylePicker.SelectedIndex));
        }
        private void Sync()
        {
            syncing = true;
            try
            {
                EnabledSwitch.IsOn = Value.Enabled; WidthSlider.Value = WidthNumber.Value = Value.Width; StylePicker.SelectedIndex = Value.Style;
                Picker.Color = Windows.UI.Color.FromArgb(255, (byte)(Value.ColorRgb >> 16), (byte)(Value.ColorRgb >> 8), (byte)Value.ColorRgb);
                WidthSlider.IsEnabled = WidthNumber.IsEnabled = StylePicker.IsEnabled = Picker.IsEnabled = Value.Enabled;
                sample.Stroke = Value.Enabled ? Ui.Brush((uint)Value.ColorRgb) : null; sample.StrokeThickness = Value.Width / scale;
                var pattern = BorderSettings.Pattern(Value.Style);
                sample.StrokeDashArray = new DoubleCollection();
                if (pattern != null) foreach (float length in pattern) sample.StrokeDashArray.Add(length);
            }
            finally { syncing = false; }
        }
        internal void SetValues(BorderSettings value) { Value = value; Sync(); preview(Value); }
        internal void ResetValues() { SetValues(resetBorder); }
        internal async Task<ContentDialogResult> ShowAsync() { var result = await Dialog.ShowAsync(); return testResult ?? result; }
        internal void Hide() { Dialog.Hide(); }
        internal void CompleteForTesting(bool confirm)
        {
            if (!Program.Testing) throw new InvalidOperationException();
            testResult = confirm ? ContentDialogResult.Primary : ContentDialogResult.None; Dialog.Hide();
        }
    }
}
