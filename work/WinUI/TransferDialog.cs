using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ScreenCurtain
{
    internal sealed class CoordinateScreen
    {
        internal string Label;
        internal Drawing.Rectangle Bounds;
        public override string ToString() { return Label + " · " + Bounds.Width + " × " + Bounds.Height + " px"; }
        internal static List<CoordinateScreen> Available()
        {
            var result = Forms.Screen.AllScreens.OrderByDescending(s => s.Primary).Select((s, i) => new CoordinateScreen
                { Label = s.Primary ? "主屏幕" : "屏幕 " + (i + 1), Bounds = s.Bounds }).ToList();
            if (result.Count > 1) result.Add(new CoordinateScreen { Label = "整个多屏桌面", Bounds = Forms.SystemInformation.VirtualScreen });
            return result;
        }
    }
    internal sealed class TransferDialog
    {
        internal readonly ContentDialog Dialog;
        internal readonly ComboBox ModePicker, ScreenPicker, RatioPicker;
        internal readonly CheckBox PixelScaling;
        internal readonly TextBox TextPreview;
        internal readonly InfoBar Status;
        internal bool IsPickingFile { get; private set; }
        internal List<Zone> ParsedZones { get; private set; } = new List<Zone>();
        internal string OutputText { get; private set; }
        internal string SavedPath { get; private set; }
        private readonly bool importing;
        private readonly Zone[] source;
        private readonly int remaining;
        private readonly IntPtr owner;
        private readonly TextBlock modeHelp, filename;
        private readonly Canvas canvas;
        private readonly Grid previewHost;
        private string loadedText;
        private ContentDialogResult? testResult;
        internal CoordinateMode Mode { get { return ModePicker.SelectedIndex == 1 ? CoordinateMode.ScreenPercent : CoordinateMode.Pixels; } }
        private Drawing.Rectangle TargetScreen { get { return ((CoordinateScreen)ScreenPicker.SelectedItem).Bounds; } }
        private Drawing.Rectangle ReferenceScreen { get { return FitReference(TargetScreen, RatioPicker.SelectedIndex); } }
        internal static Drawing.Size ReferenceSize(Drawing.Rectangle target, int index)
        {
            var sizes = new[] { Drawing.Size.Empty, new Drawing.Size(1920, 1080), new Drawing.Size(720, 576),
                new Drawing.Size(1024, 768), new Drawing.Size(1920, 1200), new Drawing.Size(2560, 1080), new Drawing.Size(1080, 1920) };
            return index <= 0 || index >= sizes.Length ? target.Size : sizes[index];
        }
        internal static Drawing.Rectangle FitReference(Drawing.Rectangle target, int index)
        {
            if (index <= 0) return target;
            var basis = ReferenceSize(target, index);
            double scale = Math.Min((double)target.Width / basis.Width, (double)target.Height / basis.Height);
            int width = Math.Max(1, (int)Math.Round(basis.Width * scale, MidpointRounding.AwayFromZero));
            int height = Math.Max(1, (int)Math.Round(basis.Height * scale, MidpointRounding.AwayFromZero));
            return new Drawing.Rectangle(target.X + (target.Width - width) / 2, target.Y + (target.Height - height) / 2, width, height);
        }

        internal TransferDialog(bool import, IEnumerable<Zone> zones, XamlRoot root, IntPtr handle)
        {
            importing = import; source = zones.ToArray(); remaining = Math.Max(0, ShapeTextCodec.MaxShapes - source.Length); owner = handle;
            var content = new StackPanel { Spacing = 12, MaxWidth = 440, RequestedTheme = ElementTheme.Light };
            content.Children.Add(Ui.Text(importing ? "直接输入或粘贴顶点坐标，每行一个形状，按顶点顺序自动闭合。" :
                "每行一个形状，按顺序保存顶点，形状会自动闭合。", 13, 0x657184));
            ModePicker = new ComboBox { Header = "坐标模式", HorizontalAlignment = HorizontalAlignment.Stretch };
            ModePicker.Items.Add("像素坐标"); ModePicker.Items.Add("屏幕比例（百分比）"); ModePicker.SelectedIndex = 0;
            content.Children.Add(ModePicker);
            PixelScaling = new CheckBox { Content = "按基准分辨率换算像素坐标", IsChecked = false };
            content.Children.Add(PixelScaling);
            ScreenPicker = new ComboBox { Header = "目标屏幕", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var screen in CoordinateScreen.Available()) ScreenPicker.Items.Add(screen);
            ScreenPicker.SelectedIndex = 0; content.Children.Add(ScreenPicker);
            RatioPicker = new ComboBox { Header = "比例基准画面", HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string label in new[] { "目标屏幕原比例", "1920 × 1080（16:9）", "720 × 576（5:4）", "1024 × 768（4:3）", "1920 × 1200（16:10）", "2560 × 1080（21:9）", "1080 × 1920（竖屏 9:16）" }) RatioPicker.Items.Add(label);
            RatioPicker.SelectedIndex = 0; content.Children.Add(RatioPicker);
            modeHelp = Ui.Text("", 12, 0x657184); content.Children.Add(modeHelp);
            TextPreview = new TextBox { Header = importing ? "顶点坐标（直接输入或粘贴）" : "TXT 内容预览", IsReadOnly = !importing,
                AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = importing ? 144 : 112,
                FontFamily = new FontFamily("Consolas"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            ScrollViewer.SetHorizontalScrollBarVisibility(TextPreview, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(TextPreview, ScrollBarVisibility.Auto);
            content.Children.Add(TextPreview);
            if (importing)
            {
                var select = Ui.Button("从 TXT 文件载入", PickImportFile, false, "\uE8E5"); content.Children.Add(select);
                filename = Ui.Text("", 12, 0x657184); filename.Visibility = Visibility.Collapsed; content.Children.Add(filename);
            }
            Status = new InfoBar { IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Informational };
            content.Children.Add(Status);
            previewHost = new Grid { Height = 112, Background = Ui.Brush(0xeef3f9), CornerRadius = new CornerRadius(6) };
            canvas = new Canvas(); previewHost.Children.Add(canvas); content.Children.Add(previewHost);
            previewHost.SizeChanged += OnPreviewSizeChanged;
            content.Children.Add(Ui.Text(importing ? "导入会追加到现有选区，新选区使用“默认设置”中的外观和边框。" :
                "导出全部 " + source.Length + " 个选区，仅保存顶点坐标。外观和开关不包含在 TXT 中。", 12, 0x657184));
            if (!importing && source.Any(z => z.Curve != CurveShape.None))
                content.Children.Add(Ui.Text("圆形、椭圆和圆角矩形会转换为多边形近似；重新导入后作为多边形处理。", 12, 0x657184));
            var scroll = new ScrollViewer { Content = content, MaxHeight = Math.Max(220, root.Size.Height - 180), Padding = new Thickness(0, 0, 12, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Disabled };
            Dialog = new ContentDialog { Title = importing ? "导入选区" : "导出选区 · TXT", Content = scroll,
                PrimaryButtonText = importing ? "导入" : "导出 TXT", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Primary,
                XamlRoot = root, RequestedTheme = ElementTheme.Light };
            Dialog.PrimaryButtonClick += OnPrimaryClick;
            ModePicker.SelectionChanged += OnModeChanged; ScreenPicker.SelectionChanged += OnModeChanged; RatioPicker.SelectionChanged += OnModeChanged;
            PixelScaling.Checked += OnPixelScalingChanged; PixelScaling.Unchecked += OnPixelScalingChanged;
            TextPreview.TextChanged += OnTextChanged;
            Recompute();
        }
        private void OnPreviewSizeChanged(object sender, SizeChangedEventArgs e) { DrawPreview(); }
        private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs e) { e.Cancel = importing ? !Recompute() : !SaveExportFile(); }
        private void OnModeChanged(object sender, SelectionChangedEventArgs e) { Recompute(); }
        private void OnPixelScalingChanged(object sender, RoutedEventArgs e) { Recompute(); }
        private void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!importing) return;
            if (TextPreview.Text != loadedText) filename.Visibility = Visibility.Collapsed;
            Recompute();
        }
        internal void Release()
        {
            previewHost.SizeChanged -= OnPreviewSizeChanged; Dialog.PrimaryButtonClick -= OnPrimaryClick;
            ModePicker.SelectionChanged -= OnModeChanged; ScreenPicker.SelectionChanged -= OnModeChanged; RatioPicker.SelectionChanged -= OnModeChanged; TextPreview.TextChanged -= OnTextChanged;
            PixelScaling.Checked -= OnPixelScalingChanged; PixelScaling.Unchecked -= OnPixelScalingChanged;
            canvas.Children.Clear(); Dialog.Content = null;
        }
        internal void LoadText(string text, string label)
        {
            TextPreview.Text = text ?? "";
            loadedText = TextPreview.Text;
            if (filename != null) { filename.Text = "已载入：" + label + "（可直接修改）"; filename.Visibility = Visibility.Visible; }
            Recompute();
        }
        private bool Recompute()
        {
            bool pixelScaled = importing && Mode == CoordinateMode.Pixels && PixelScaling.IsChecked == true;
            PixelScaling.Visibility = importing && Mode == CoordinateMode.Pixels ? Visibility.Visible : Visibility.Collapsed;
            ScreenPicker.Visibility = Mode == CoordinateMode.ScreenPercent || pixelScaled ? Visibility.Visible : Visibility.Collapsed;
            RatioPicker.Visibility = ScreenPicker.Visibility;
            modeHelp.Text = Mode == CoordinateMode.Pixels ? (pixelScaled ? "坐标以基准画面左上角为原点，等比例适配目标屏幕并居中。\n示例：[100,100],[500,100],[300,400]" : "绝对像素坐标，可使用多屏桌面的负坐标。\n示例：[100,100],[500,100],[300,400]") :
                "基准画面等比例适配目标屏幕并居中，可能在左右或上下留边。百分比以画面左上角为 [0%,0%]，25 表示 25%。\n示例：[10%,10%],[50%,10%],[30%,60%]";
            if (importing) TextPreview.PlaceholderText = Mode == CoordinateMode.Pixels ?
                "[100,100],[500,100],[300,400]\r\n[600,100],[800,100],[800,300],[600,300]" :
                "[10%,10%],[50%,10%],[30%,60%]\r\n[60%,10%],[80%,10%],[80%,30%],[60%,30%]";
            ParsedZones = new List<Zone>(); OutputText = null;
            if (importing && string.IsNullOrWhiteSpace(TextPreview.Text))
            {
                Dialog.IsPrimaryButtonEnabled = false; Status.IsOpen = false;
                previewHost.Visibility = Visibility.Collapsed; return false;
            }
            try
            {
                if (importing)
                {
                    ParsedZones = ShapeTextCodec.Parse(TextPreview.Text, Mode, ReferenceScreen);
                    if (pixelScaled) ParsedZones = ShapeTextCodec.ScalePixels(ParsedZones, ReferenceSize(TargetScreen, RatioPicker.SelectedIndex), ReferenceScreen);
                    if (ParsedZones.Count > remaining) throw new FormatException("本次包含 " + ParsedZones.Count + " 个形状，当前还可添加 " + remaining + " 个选区（最多 100 个）。");
                }
                else
                {
                    OutputText = ShapeTextCodec.Serialize(source, Mode, ReferenceScreen);
                    ParsedZones = source.ToList(); TextPreview.Text = PreviewText(OutputText);
                }
                int rectangles = ParsedZones.Count(z => !z.Polygon && z.Curve == CurveShape.None);
                int curves = ParsedZones.Count(z => z.Curve != CurveShape.None);
                Status.Title = importing ? "已识别 " + ParsedZones.Count + " 个形状" : "准备导出 " + ParsedZones.Count + " 个形状";
                Status.Message = rectangles + " 个矩形，" + (ParsedZones.Count - rectangles - curves) + " 个多边形" + (curves > 0 ? "，" + curves + " 个曲线形状。" : "。");
                var viewport = Mode == CoordinateMode.ScreenPercent || pixelScaled ? ReferenceScreen : Forms.SystemInformation.VirtualScreen;
                int outside = ParsedZones.Count(z => !viewport.Contains(z.Bounds));
                if (outside > 0) Status.Message += " " + outside + " 个形状超出预览屏幕，预览可能只显示一部分。";
                Status.Severity = InfoBarSeverity.Success; Status.IsOpen = true;
                Dialog.IsPrimaryButtonEnabled = true; previewHost.Visibility = TextPreview.Visibility = Visibility.Visible; DrawPreview(); return true;
            }
            catch (Exception ex) when (ex is FormatException || ex is System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                ShowError(ex is FormatException ? ex.Message : "这一行格式过长或复杂，请使用 [x,y] 格式。"); return false;
            }
        }
        private static string PreviewText(string text) { return text.Length <= 12000 ? text : text.Substring(0, 12000) + "\r\n…（预览已截短，文件内容会完整处理）"; }
        private void ShowError(string message)
        {
            ParsedZones = new List<Zone>(); OutputText = null; Dialog.IsPrimaryButtonEnabled = false;
            Status.Title = "无法" + (importing ? "导入" : "导出"); Status.Message = message; Status.Severity = InfoBarSeverity.Error; Status.IsOpen = true;
            previewHost.Visibility = Visibility.Collapsed;
        }
        private void DrawPreview()
        {
            canvas.Children.Clear();
            if (ParsedZones.Count == 0 || previewHost.ActualWidth < 20) return;
            bool fitted = Mode == CoordinateMode.ScreenPercent || (importing && PixelScaling.IsChecked == true);
            var screen = fitted ? TargetScreen : Forms.SystemInformation.VirtualScreen;
            double width = previewHost.ActualWidth, height = previewHost.Height;
            canvas.Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) };
            double scale = Math.Min((width - 20) / screen.Width, (height - 20) / screen.Height);
            double left = (width - screen.Width * scale) / 2, top = (height - screen.Height * scale) / 2;
            var frame = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = screen.Width * scale, Height = screen.Height * scale,
                Fill = Ui.Brush(0xffffff), Stroke = Ui.Brush(0xc4cbd5), StrokeThickness = 1 };
            Canvas.SetLeft(frame, left); Canvas.SetTop(frame, top); canvas.Children.Add(frame);
            if (fitted && ReferenceScreen != screen)
            {
                var basis = ReferenceScreen;
                var area = new Microsoft.UI.Xaml.Shapes.Rectangle { Width = basis.Width * scale, Height = basis.Height * scale,
                    Fill = Ui.Brush(0xf3f7ff), Stroke = Ui.Brush(0x176be8), StrokeThickness = 1 };
                Canvas.SetLeft(area, left + (basis.X - screen.X) * scale); Canvas.SetTop(area, top + (basis.Y - screen.Y) * scale); canvas.Children.Add(area);
            }
            foreach (var zone in ParsedZones)
            {
                var shape = new Microsoft.UI.Xaml.Shapes.Polygon { Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(65, 23, 107, 232)), Stroke = Ui.Brush(0x176be8), StrokeThickness = 1.5 };
                foreach (var point in ShapeGeometry.ExportPoints(zone)) shape.Points.Add(new Point(left + ((double)point.X - screen.X) * scale, top + ((double)point.Y - screen.Y) * scale));
                canvas.Children.Add(shape);
            }
        }
        private void PickImportFile()
        {
            SynchronizationContext previous = SynchronizationContext.Current; IsPickingFile = true;
            try
            {
                using (var picker = new Forms.OpenFileDialog { Title = "导入选区 TXT", Filter = "TXT 文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*", CheckFileExists = true, Multiselect = false, RestoreDirectory = true })
                {
                    if (picker.ShowDialog(new WindowOwner(owner)) == Forms.DialogResult.OK)
                        LoadText(ShapeTextCodec.ReadFile(picker.FileName), System.IO.Path.GetFileName(picker.FileName));
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is System.Text.DecoderFallbackException)
            {
                ShowError("读取失败：" + ex.Message);
            }
            finally { IsPickingFile = false; SynchronizationContext.SetSynchronizationContext(previous); }
        }
        private bool SaveExportFile()
        {
            if (!Recompute()) return false;
            SynchronizationContext previous = SynchronizationContext.Current; IsPickingFile = true;
            try
            {
                using (var picker = new Forms.SaveFileDialog { Title = "导出选区 TXT", Filter = "TXT 文本文件 (*.txt)|*.txt", AddExtension = true, DefaultExt = "txt", OverwritePrompt = true,
                    FileName = Mode == CoordinateMode.Pixels ? "遮罩选区-像素坐标.txt" : "遮罩选区-屏幕比例.txt", RestoreDirectory = true })
                {
                    if (picker.ShowDialog(new WindowOwner(owner)) != Forms.DialogResult.OK) return false;
                    ShapeTextCodec.WriteFile(picker.FileName, OutputText); SavedPath = picker.FileName; return true;
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                Status.Title = "导出失败"; Status.Message = ex.Message; Status.Severity = InfoBarSeverity.Error; Status.IsOpen = true; return false;
            }
            finally { IsPickingFile = false; SynchronizationContext.SetSynchronizationContext(previous); }
        }
        internal async Task<ContentDialogResult> ShowAsync() { var result = await Dialog.ShowAsync(); return testResult ?? result; }
        internal void Hide() { Dialog.Hide(); }
        internal void CompleteForTesting(bool confirm)
        {
            if (!Program.Testing || (confirm && (!importing || !Dialog.IsPrimaryButtonEnabled))) throw new InvalidOperationException();
            testResult = confirm ? ContentDialogResult.Primary : ContentDialogResult.None; Dialog.Hide();
        }
        private sealed class WindowOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            internal WindowOwner(IntPtr handle) { Handle = handle; }
        }
    }
}
