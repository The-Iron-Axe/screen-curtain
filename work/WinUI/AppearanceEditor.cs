using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScreenCurtain
{
    internal sealed class AppearanceEditor
    {
        internal readonly ContentDialog Dialog;
        internal ColorPicker Picker { get; private set; }
        internal Slider OpacitySlider { get; private set; }
        internal NumberBox OpacityNumber { get; private set; }
        private readonly HyperlinkButton reset;
        internal int ColorRgb { get; private set; }
        internal int OpacityPercent { get; private set; }
        private readonly Action<int, int> preview;
        private readonly ZoneStyle resetStyle;
        private bool syncing;
        private ContentDialogResult? testResult;
        internal AppearanceEditor(Zone zone, XamlRoot root, Action<int, int> previewAction, Zone defaults = null, bool editingDefaults = false)
        {
            preview = previewAction;
            resetStyle = ZoneStyle.FromZone(defaults ?? new Zone());
            ColorRgb = Appearance.ValidRgb(zone.ColorRgb); OpacityPercent = Appearance.ValidOpacity(zone.OpacityPercent);
            var content = new StackPanel { Spacing = 14, MaxWidth = 360, RequestedTheme = ElementTheme.Light };
            content.Children.Add(Ui.Text(editingDefaults ? "设置将用于之后框选、导入的新遮罩，点击确定后保存。" : "修改会实时预览，点击确定后保存。", 13, 0x657184));
            Picker = new ColorPicker { IsAlphaEnabled = false, IsAlphaTextInputVisible = false, IsMoreButtonVisible = false,
                IsColorPreviewVisible = true, IsColorSliderVisible = true, IsColorChannelTextInputVisible = false, IsHexInputVisible = true,
                HorizontalAlignment = HorizontalAlignment.Center, Width = 300, MaxWidth = 300 };
            var opacityTitle = Ui.Text("不透明度", 14); opacityTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            content.Children.Add(opacityTitle);
            var opacityRow = Ui.Columns(Ui.Star, Ui.Auto, Ui.Auto);
            OpacitySlider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center, MinWidth = 120 };
            OpacityNumber = new NumberBox { Minimum = 0, Maximum = 100, SmallChange = 1, LargeChange = 10,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 88, VerticalAlignment = VerticalAlignment.Center };
            Ui.Put(opacityRow, OpacitySlider, 0, 0); Ui.Put(opacityRow, OpacityNumber, 0, 1); Ui.Put(opacityRow, Ui.Text("%"), 0, 2);
            content.Children.Add(opacityRow);
            content.Children.Add(Ui.Text("0% 完全透明，100% 完全不透明。", 12, 0x657184));
            var colorTitle = Ui.Text("遮罩颜色", 14); colorTitle.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            content.Children.Add(colorTitle); content.Children.Add(Picker);
            reset = new HyperlinkButton { Content = editingDefaults ? "恢复初始设置" : "使用默认外观", Padding = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
            reset.Click += OnResetClick; content.Children.Add(reset);
            var scroller = new ScrollViewer { Content = content, Padding = new Thickness(0, 0, 12, 0), MaxHeight = Math.Max(220, root.Size.Height - 180), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
            Dialog = new ContentDialog { Title = editingDefaults ? "新建遮罩 · 默认外观" : zone.DisplayName + " · 外观设置", Content = scroller, PrimaryButtonText = "确定", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = root, RequestedTheme = ElementTheme.Light };
            Picker.ColorChanged += OnColorChanged;
            OpacitySlider.ValueChanged += OnSliderChanged;
            OpacityNumber.ValueChanged += OnNumberChanged;
            Sync();
        }
        private void OnColorChanged(ColorPicker sender, ColorChangedEventArgs e) { if (!syncing) SetValues((e.NewColor.R << 16) | (e.NewColor.G << 8) | e.NewColor.B, OpacityPercent); }
        private void OnSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) { if (!syncing) SetValues(ColorRgb, (int)Math.Round(e.NewValue)); }
        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs e) { if (!syncing && double.IsFinite(e.NewValue)) SetValues(ColorRgb, (int)Math.Round(e.NewValue)); }
        private void OnResetClick(object sender, RoutedEventArgs e) { ResetValues(); }
        internal void Release()
        {
            Picker.ColorChanged -= OnColorChanged; OpacitySlider.ValueChanged -= OnSliderChanged; OpacityNumber.ValueChanged -= OnNumberChanged;
            reset.Click -= OnResetClick;
            Dialog.Content = null;
            Picker = null; OpacitySlider = null; OpacityNumber = null;
        }
        private void Sync()
        {
            syncing = true;
            try
            {
                Picker.Color = Windows.UI.Color.FromArgb(255, (byte)(ColorRgb >> 16), (byte)(ColorRgb >> 8), (byte)ColorRgb);
                OpacitySlider.Value = OpacityPercent; OpacityNumber.Value = OpacityPercent;
            }
            finally { syncing = false; }
        }
        internal void SetValues(int rgb, int percent)
        {
            ColorRgb = Appearance.ValidRgb(rgb); OpacityPercent = Appearance.ValidOpacity(percent); Sync(); preview(ColorRgb, OpacityPercent);
        }
        internal void ResetValues() { SetValues(resetStyle.ColorRgb, resetStyle.OpacityPercent); }
        internal async Task<ContentDialogResult> ShowAsync() { var result = await Dialog.ShowAsync(); return testResult ?? result; }
        internal void Hide() { Dialog.Hide(); }
        internal void CompleteForTesting(bool confirm)
        {
            if (!Program.Testing) throw new InvalidOperationException();
            testResult = confirm ? ContentDialogResult.Primary : ContentDialogResult.None; Dialog.Hide();
        }
    }
}
