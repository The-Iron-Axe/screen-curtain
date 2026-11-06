using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Forms = System.Windows.Forms;

namespace ScreenCurtain
{
    internal sealed class MainWindow : Window
    {
        internal readonly bool Testing;
        internal readonly Grid Root;
        internal readonly List<Zone> Zones = new List<Zone>();
        internal readonly Dictionary<Zone, MaskForm> Masks = new Dictionary<Zone, MaskForm>();
        internal readonly ToggleSwitch PassThrough;
        internal readonly Expander Shortcuts;
        internal AppearanceEditor Editor;
        internal BorderEditor BorderEditor;
        internal TransferDialog Transfer;
        internal RenameEditor RenameEditor;
        internal readonly IntPtr Handle;
        internal bool Master = true;
        internal ZoneStyle DefaultStyle { get; private set; } = ZoneStyle.FromZone(new Zone());
        private readonly StackPanel cards = new StackPanel { Spacing = 10 };
        private readonly Dictionary<Zone, ToggleSwitch> zoneSwitches = new Dictionary<Zone, ToggleSwitch>();
        private readonly Dictionary<Zone, Button> pasteButtons = new Dictionary<Zone, Button>();
        private readonly Dictionary<Zone, (CardStamp Stamp, Border Card)> cardCache = new Dictionary<Zone, (CardStamp, Border)>();
        private readonly record struct CardStamp(int Number, string Name, bool Polygon, CurveShape Curve, int Vertices,
            System.Drawing.Rectangle Bounds, ZoneStyle Style);
        private Border emptyCard;
        private readonly Grid body, listArea, listHeader;
        internal readonly ScrollViewer CardScroll, PageScroll;
        private readonly Border cardProbe;
        private readonly Expander shortcutsProbe;
        private readonly StackPanel measurementPanel;
        private ZoneStyle? copiedStyle;
        private bool layoutQueued, updatingConstraints;
        private int minimumWidthPx, minimumHeightPx;
        internal double RequiredCardHeight { get; private set; }
        internal double RequiredWindowHeight { get; private set; }
        internal bool UsesPageScrolling { get { return PageScroll.VerticalScrollMode == ScrollMode.Enabled; } }
        internal double? TestWorkAreaHeight { get; set; }
        internal SizeInt32 MinimumWindowSize { get { return new SizeInt32(minimumWidthPx, minimumHeightPx); } }
        private readonly TextBlock count, state;
        private readonly Button masterButton, undoButton, clearButton, importButton, exportButton;
        private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        private readonly Forms.ContextMenuStrip trayMenu = new Forms.ContextMenuStrip();
        private readonly Forms.ToolStripMenuItem trayToggle = new Forms.ToolStripMenuItem();
        private readonly Forms.ToolStripMenuItem traySpotlight = new Forms.ToolStripMenuItem("开启聚光灯");
        private readonly MenuFlyoutItem spotlightMenu = new MenuFlyoutItem { Text = "开启聚光灯" };
        private readonly MenuFlyoutItem followSpotlightMenu = new MenuFlyoutItem { Text = "开启鼠标跟随聚光灯" };
        private readonly MenuFlyoutItem circleSpotlightMenu = new MenuFlyoutItem { Text = "开启圆形固定聚光灯" };
        private readonly MenuFlyoutItem circleFollowSpotlightMenu = new MenuFlyoutItem { Text = "开启圆形跟随聚光灯" };
        private readonly Forms.ToolStripMenuItem trayCircleSpotlight = new Forms.ToolStripMenuItem("开启圆形固定聚光灯");
        private readonly Forms.ToolStripMenuItem trayCircleFollowSpotlight = new Forms.ToolStripMenuItem("开启圆形跟随聚光灯");
        private readonly Forms.ToolStripMenuItem trayFollowSpotlight = new Forms.ToolStripMenuItem("开启鼠标跟随聚光灯");
        private readonly List<SpotlightForm> spotlights = new List<SpotlightForm>();
        private readonly DispatcherQueueTimer spotlightTimer;
        private readonly DispatcherQueueTimer temporarySpotlightTimer;
        private readonly List<SpotlightForm> temporarySpotlights = new List<SpotlightForm>();
        private TemporarySpotlightWheel temporaryWheel;
        private int temporaryDiameter = 300, wheelRemainder;
        internal int TemporaryDiameter { get { return temporaryDiameter; } }
        internal int TemporarySpotlightCount { get { return temporarySpotlights.Count; } }
        private System.Drawing.Point? temporaryCursor;
        private bool spotlightFollow;
        private bool spotlightCircle;
        private System.Drawing.Size spotlightSize;
        private System.Drawing.Point? spotlightCursor;
        private readonly DispatcherQueueTimer keeper;
        private readonly DispatcherQueueTimer resourceCleanup;
        internal int CardCreationCount { get; private set; }
        internal int ResourceCleanupCount { get; private set; }
        internal int CachedCardCount { get { return cardCache.Count; } }
        private readonly WindowOrderWatcher orderWatcher;
        private bool orderQueued;
        private readonly DesktopInterop.SubclassProc subclass;
        private readonly List<int> hotkeys = new List<int>();
        private Forms.Form selection;
        private bool selecting, closing, initialized, testStarted, syncingSwitch, exitRequested;
        internal bool IsSelecting { get { return selecting; } }
        internal bool ExitRequested { get { return exitRequested; } }
        internal bool ControllerClosed { get; private set; }
        private bool HasSettingsDialog { get { return Editor != null || BorderEditor != null || Transfer != null || RenameEditor != null; } }
        private int NextNumber { get { return Zones.Count == 0 ? 1 : Zones.Max(z => z.Number) + 1; } }
        private string warning = "";
        private string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCurtain", "settings.xml"); } }

        internal MainWindow(bool testing)
        {
            Testing = testing; Title = "屏幕遮罩";
            Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Root = new Grid { Name = "MainRoot", RequestedTheme = ElementTheme.Light, Background = Ui.Brush(0xf7f9fc) };
            Root.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto }); Root.RowDefinitions.Add(new RowDefinition { Height = Ui.Star });
            Content = Root;
            ExtendsContentIntoTitleBar = true;
            var titleBar = new Grid { Height = 48, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding = new Thickness(18, 0, 145, 0) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            var brandIcon = Ui.Icon("\uE7F4", 16); brandIcon.Foreground = Ui.Brush(0x176be8); brand.Children.Add(brandIcon);
            brand.Children.Add(Ui.Text("屏幕遮罩", 13)); titleBar.Children.Add(brand); Ui.Put(Root, titleBar, 0); SetTitleBar(titleBar);
            AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
            AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 25, 35, 49);
            AppWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 120, 130, 145);
            AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 230, 234, 240);
            AppWindow.TitleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Black;
            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = false;
            try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ScreenCurtain.ico")); } catch { }

            body = new Grid { Padding = new Thickness(24, 12, 24, 20), RowSpacing = 16 };
            foreach (GridLength height in new[] { Ui.Auto, Ui.Auto, Ui.Auto, Ui.Auto, Ui.Star, Ui.Auto })
                body.RowDefinitions.Add(new RowDefinition { Height = height });
            PageScroll = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled };
            Ui.Put(Root, PageScroll, 1);
            var addRow = Ui.Columns(Ui.Star, Ui.Star, Ui.Auto);
            var rectangle = Ui.Button("矩形框选", () => BeginSelection(false), true, "\uE739");
            var polygon = Ui.Button("多边形框选", () => BeginSelection(true), false, "\uE70F");
            ToolTipService.SetToolTip(rectangle, "按住鼠标拖拽 · Ctrl+Alt+R");
            ToolTipService.SetToolTip(polygon, "单击添加顶点，靠近起点后单击闭合；Enter / 双击完成，Esc 取消 · Ctrl+Alt+P");
            var more = new Button { Name = "MoreShapes", Content = Ui.Icon("\uE712", 16), Padding = new Thickness(9), MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Stretch };
            var shapeMenu = new MenuFlyout();
            foreach (var item in new[] { ("椭圆 / 圆形", SelectionShape.Ellipse), ("三角形", SelectionShape.Triangle), ("圆角矩形", SelectionShape.RoundedRectangle) })
            {
                var entry = new MenuFlyoutItem { Text = item.Item1 };
                SelectionShape kind = item.Item2; entry.Click += (s, e) => BeginSelection(kind); shapeMenu.Items.Add(entry);
            }
            shapeMenu.Items.Add(new MenuFlyoutSeparator()); spotlightMenu.Click += (s, e) => ToggleSpotlight(); shapeMenu.Items.Add(spotlightMenu);
            followSpotlightMenu.Click += (s, e) => ToggleSpotlight(true); shapeMenu.Items.Add(followSpotlightMenu);
            circleSpotlightMenu.Click += (s, e) => ToggleSpotlight(false, true); shapeMenu.Items.Add(circleSpotlightMenu);
            circleFollowSpotlightMenu.Click += (s, e) => ToggleSpotlight(true, true); shapeMenu.Items.Add(circleFollowSpotlightMenu);
            more.Flyout = shapeMenu; ToolTipService.SetToolTip(more, "更多形状与聚光灯模式");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "更多形状");
            Ui.Put(addRow, rectangle, 0, 0); Ui.Put(addRow, polygon, 0, 1); Ui.Put(addRow, more, 0, 2); Ui.Put(body, addRow, 0);
            var actionRow = Ui.Columns(Ui.Star, Ui.Auto, Ui.Auto);
            masterButton = Ui.Button("关闭已选中的遮罩", ToggleMaster);
            ToolTipService.SetToolTip(masterButton, "只控制下方独立开关已打开的选区；各选区的开关状态会保留。Ctrl+Alt+B");
            undoButton = Ui.Button("撤销", () => { if (Zones.Count > 0) RemoveZone(Zones[Zones.Count - 1]); });
            clearButton = Ui.Button("清空", ClearZones);
            Ui.Put(actionRow, masterButton, 0, 0); Ui.Put(actionRow, undoButton, 0, 1); Ui.Put(actionRow, clearButton, 0, 2); Ui.Put(body, actionRow, 1);
            state = Ui.Text("就绪", 14, 0x285487);
            state.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            Ui.Put(body, new Border { Background = Ui.Brush(0xebf2fc), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 10, 12, 10), Child = state }, 2);
            var optionRow = Ui.Columns(Ui.Star, Ui.Auto);
            var optionText = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            optionText.Children.Add(Ui.Text("鼠标穿透", 14)); optionText.Children.Add(Ui.Text("允许操作遮罩下方的窗口", 12, 0x657184));
            PassThrough = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(PassThrough, "允许鼠标穿透遮罩");
            PassThrough.Toggled += (s, e) => { if (initialized) { RefreshMasks(); SaveSettings(); } };
            Ui.Put(optionRow, optionText, 0, 0); Ui.Put(optionRow, PassThrough, 0, 1); Ui.Put(body, optionRow, 3);

            listArea = new Grid { RowSpacing = 10 };
            listArea.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto }); listArea.RowDefinitions.Add(new RowDefinition { Height = Ui.Star });
            listHeader = Ui.Columns(Ui.Star, Ui.Auto, Ui.Auto, Ui.Auto);
            count = Ui.Text("选区 0", 13, 0x657184); Ui.Put(listHeader, count, 0);
            var defaultsButton = Ui.Button("默认设置", () => { }); defaultsButton.Name = "DefaultSettings";
            defaultsButton.Padding = new Thickness(10, 6, 10, 6);
            var defaultsMenu = new MenuFlyout();
            var defaultAppearance = new MenuFlyoutItem { Text = "默认外观设置" }; defaultAppearance.Click += (s, e) => _ = OpenDefaultAppearanceAsync();
            var defaultBorder = new MenuFlyoutItem { Text = "默认边框设置" }; defaultBorder.Click += (s, e) => _ = OpenDefaultBorderAsync();
            defaultsMenu.Items.Add(defaultAppearance); defaultsMenu.Items.Add(defaultBorder); defaultsButton.Flyout = defaultsMenu;
            ToolTipService.SetToolTip(defaultsButton, "设置之后框选、导入的新遮罩的默认外观与边框");
            importButton = Ui.Button("导入", () => _ = OpenTransferAsync(true));
            exportButton = Ui.Button("导出", () => _ = OpenTransferAsync(false));
            importButton.Padding = exportButton.Padding = new Thickness(10, 6, 10, 6);
            ToolTipService.SetToolTip(importButton, "输入或粘贴顶点坐标，也可从 TXT 追加选区，支持像素坐标和屏幕比例");
            ToolTipService.SetToolTip(exportButton, "将全部选区的顶点坐标保存为 TXT");
            Ui.Put(listHeader, defaultsButton, 0, 1); Ui.Put(listHeader, importButton, 0, 2); Ui.Put(listHeader, exportButton, 0, 3); Ui.Put(listArea, listHeader, 0);
            CardScroll = new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Auto, Padding = new Thickness(0, 0, 4, 0) };
            Ui.Put(listArea, CardScroll, 1); Ui.Put(body, listArea, 4);

            Shortcuts = CreateShortcuts(false);
            Ui.Put(body, Shortcuts, 5);

            // Rooted probes inherit the real theme and text scaling, without drawing or taking layout space.
            cardProbe = CreateZoneCard(new Zone { Number = 100, Polygon = true, BorderEnabled = true, BorderWidth = 50, BorderStyle = 4,
                Points = new List<System.Drawing.Point> { new System.Drawing.Point(-1920, -1080), new System.Drawing.Point(3840, -1080), new System.Drawing.Point(0, 2160) } }, false);
            shortcutsProbe = CreateShortcuts(true); shortcutsProbe.IsTabStop = false; shortcutsProbe.IsEnabled = false;
            measurementPanel = new StackPanel(); measurementPanel.Children.Add(cardProbe); measurementPanel.Children.Add(shortcutsProbe);
            var measurementHost = new Canvas { Width = 0, Height = 0, Opacity = 0, IsHitTestVisible = false,
                Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 0, 0) } };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(measurementHost, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            measurementHost.Children.Add(measurementPanel); Ui.Put(Root, measurementHost, 1);
            Root.SizeChanged += (s, e) => QueueLayoutConstraints();
            cards.SizeChanged += (s, e) => QueueLayoutConstraints();
            foreach (FrameworkElement element in body.Children) element.SizeChanged += (s, e) => QueueLayoutConstraints();

            trayMenu.Items.Add("打开控制面板", null, (s, e) => ShowController());
            trayToggle.Click += (s, e) => ToggleMaster(); trayMenu.Items.Add(trayToggle); trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add("矩形框选", null, (s, e) => BeginSelection(false)); trayMenu.Items.Add("多边形框选", null, (s, e) => BeginSelection(true));
            var trayShapes = new Forms.ToolStripMenuItem("更多形状");
            trayShapes.DropDownItems.Add("椭圆 / 圆形", null, (s, e) => BeginSelection(SelectionShape.Ellipse));
            trayShapes.DropDownItems.Add("三角形", null, (s, e) => BeginSelection(SelectionShape.Triangle));
            trayShapes.DropDownItems.Add("圆角矩形", null, (s, e) => BeginSelection(SelectionShape.RoundedRectangle)); trayMenu.Items.Add(trayShapes);
            traySpotlight.Click += (s, e) => ToggleSpotlight(); trayMenu.Items.Add(traySpotlight);
            trayFollowSpotlight.Click += (s, e) => ToggleSpotlight(true); trayMenu.Items.Add(trayFollowSpotlight);
            trayCircleSpotlight.Click += (s, e) => ToggleSpotlight(false, true); trayMenu.Items.Add(trayCircleSpotlight);
            trayCircleFollowSpotlight.Click += (s, e) => ToggleSpotlight(true, true); trayMenu.Items.Add(trayCircleFollowSpotlight);
            trayMenu.Items.Add(new Forms.ToolStripSeparator()); trayMenu.Items.Add("退出", null, (s, e) => RequestExit());
            tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath); tray.Text = "屏幕遮罩"; tray.ContextMenuStrip = trayMenu;
            tray.DoubleClick += (s, e) => ShowController(); tray.Visible = !Testing;
            subclass = WindowProc; DesktopInterop.SetWindowSubclass(Handle, subclass, new UIntPtr(1), UIntPtr.Zero);
            if (!Testing)
            {
                LoadSettings();
                Forms.Keys[] keys = { Forms.Keys.B, Forms.Keys.R, Forms.Keys.P, Forms.Keys.Q, Forms.Keys.Space };
                var unavailable = new List<string>();
                for (int i = 0; i < keys.Length; i++)
                    if (Native.RegisterHotKey(Handle, i + 1, 0x4003, (uint)keys[i])) hotkeys.Add(i + 1); else unavailable.Add("Ctrl+Alt+" + keys[i]);
                if (unavailable.Count > 0) warning = "快捷键被占用：" + string.Join("、", unavailable);
            }
            initialized = true;
            RebuildCards(); RefreshMasks();
            keeper = DispatcherQueue.CreateTimer(); keeper.Interval = TimeSpan.FromSeconds(1);
            keeper.Tick += (s, e) => MaintainOverlayOrder();
            spotlightTimer = DispatcherQueue.CreateTimer(); spotlightTimer.Interval = TimeSpan.FromMilliseconds(33);
            spotlightTimer.Tick += (s, e) => UpdateFollowingSpotlight();
            temporarySpotlightTimer = DispatcherQueue.CreateTimer(); temporarySpotlightTimer.Interval = TimeSpan.FromMilliseconds(33);
            temporarySpotlightTimer.Tick += (s, e) => { if (!TemporarySpotlightWheel.ShortcutHeld()) EndTemporarySpotlight(); else UpdateTemporarySpotlight(); };
            resourceCleanup = DispatcherQueue.CreateTimer(); resourceCleanup.Interval = TimeSpan.FromSeconds(2); resourceCleanup.IsRepeating = false;
            resourceCleanup.Tick += (s, e) => ReleaseIdleResources();
            if (!Testing) { orderWatcher = new WindowOrderWatcher(QueueOverlayOrder); keeper.Start(); }
            Closed += (s, e) => { ControllerClosed = true; Shutdown(); Microsoft.UI.Xaml.Application.Current.Exit(); };
            var working = Forms.Screen.PrimaryScreen.WorkingArea;
            double scale = DesktopInterop.GetDpiForWindow(Handle) / 96.0;
            int width = Math.Min((int)(520 * scale), working.Width - 40), heightPx = Math.Min((int)(720 * scale), working.Height - 40);
            AppWindow.MoveAndResize(new RectInt32(Testing ? working.Right + 500 : working.X + (working.Width - width) / 2,
                Testing ? working.Bottom + 500 : working.Y + (working.Height - heightPx) / 2, width, heightPx));
            Root.Loaded += async (s, e) =>
            {
                Root.XamlRoot.Changed += (sender, args) => QueueLayoutConstraints();
                Root.UpdateLayout(); UpdateLayoutConstraints();
                ScheduleResourceCleanup();
                if (Testing && !testStarted)
                {
                    testStarted = true;
                    if (Program.Arguments[0] == "--self-test-resources") await ResourceTests.RunAsync(this, Program.Arguments[1]);
                    else if (Program.Arguments[0] == "--self-test-numbering") await SelfTest.RunNumberingAsync(this, Program.Arguments[1]);
                    else await SelfTest.RunAsync(this, Program.Arguments[1]);
                }
            };
        }
        private IntPtr WindowProc(IntPtr h, uint msg, IntPtr w, IntPtr l, UIntPtr id, UIntPtr data)
        {
            if (msg == 0x0010 && !exitRequested && !closing)
            {
                DispatcherQueue.TryEnqueue(HideToTray); return IntPtr.Zero;
            }
            if (msg == Native.ShowMessage) { DispatcherQueue.TryEnqueue(ShowController); return IntPtr.Zero; }
            if (msg == 0x0005 && w.ToInt32() == 1) DispatcherQueue.TryEnqueue(ScheduleResourceCleanup);
            if (msg == Native.WM_HOTKEY)
            {
                int key = w.ToInt32();
                DispatcherQueue.TryEnqueue(() => { if (key == 1) ToggleMaster(); else if (key == 2) BeginSelection(false); else if (key == 3) BeginSelection(true); else if (key == 4) RequestExit(); else if (key == 5 && TemporarySpotlightWheel.ShortcutHeld()) BeginTemporarySpotlight(); });
                return IntPtr.Zero;
            }
            if (msg == 0x0024 && l != IntPtr.Zero)
            {
                var limits = Marshal.PtrToStructure<DesktopInterop.MinMax>(l);
                double scale = DesktopInterop.GetDpiForWindow(h) / 96.0;
                var work = Forms.Screen.FromHandle(h).WorkingArea;
                limits.MinTrack.X = Math.Min(work.Width, minimumWidthPx > 0 ? minimumWidthPx : (int)(420 * scale));
                limits.MinTrack.Y = Math.Min(work.Height, minimumHeightPx > 0 ? minimumHeightPx : (int)(600 * scale));
                Marshal.StructureToPtr(limits, l, false); return IntPtr.Zero;
            }
            return DesktopInterop.DefSubclassProc(h, msg, w, l);
        }
        private static Expander CreateShortcuts(bool expanded)
        {
            var content = new StackPanel { Spacing = 7 };
            foreach (string label in new[] { "Ctrl+Alt+B   打开 / 关闭已选遮罩", "Ctrl+Alt+Q   退出并移除遮罩", "Ctrl+Alt+R   矩形框选", "Ctrl+Alt+P   多边形框选", "按住 Ctrl+Alt+空格   临时聚光 · 滚轮调大小" })
                content.Children.Add(Ui.Text(label, 12, 0x657184));
            return new Expander { Header = "快捷键", Content = content, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom,
                ExpandDirection = ExpandDirection.Up, IsExpanded = expanded };
        }
        private void QueueLayoutConstraints()
        {
            if (closing || !Root.IsLoaded || layoutQueued || updatingConstraints) return;
            layoutQueued = true;
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                layoutQueued = false;
                if (!closing) { Root.UpdateLayout(); UpdateLayoutConstraints(); }
            });
        }
        internal void UpdateLayoutConstraints()
        {
            if (closing || updatingConstraints || Root.ActualWidth < 1 || cardProbe == null) return;
            if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized) return;
            updatingConstraints = true;
            try
            {
                double width = Math.Max(1, body.ActualWidth - body.Padding.Left - body.Padding.Right);
                // Reserve scrollbar width even with one zone, so adding more zones cannot invalidate the minimum.
                cardProbe.Width = Math.Max(1, width - CardScroll.Padding.Right - 16);
                shortcutsProbe.Width = width;
                measurementPanel.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
                RequiredCardHeight = Math.Max(cardProbe.DesiredSize.Height,
                    cards.Children.OfType<FrameworkElement>().Select(c => c.DesiredSize.Height).DefaultIfEmpty(0).Max());
                double fixedRows = body.Children.OfType<FrameworkElement>().Where(c => c != listArea && c != Shortcuts).Sum(c => c.DesiredSize.Height);
                RequiredWindowHeight = 48 + body.Padding.Top + body.Padding.Bottom + body.RowSpacing * (body.RowDefinitions.Count - 1) + fixedRows +
                    listHeader.DesiredSize.Height + listArea.RowSpacing + RequiredCardHeight + shortcutsProbe.DesiredSize.Height + 8;
                double scale = Root.XamlRoot.RasterizationScale;
                var work = Forms.Screen.FromHandle(Handle).WorkingArea;
                int workHeight = Testing && TestWorkAreaHeight.HasValue ? Math.Min(work.Height, (int)(TestWorkAreaHeight.Value * scale)) : work.Height;
                int chromeHeight = Math.Max(0, AppWindow.Size.Height - AppWindow.ClientSize.Height);
                int chromeWidth = Math.Max(0, AppWindow.Size.Width - AppWindow.ClientSize.Width);
                int requiredPx = (int)Math.Ceiling(RequiredWindowHeight * scale) + chromeHeight;
                minimumWidthPx = Math.Min(work.Width, (int)Math.Ceiling(420 * scale) + chromeWidth);
                minimumHeightPx = Math.Min(workHeight, requiredPx);
                bool fallback = requiredPx > workHeight;
                body.Height = fallback ? RequiredWindowHeight - 48 : double.NaN;
                PageScroll.VerticalScrollMode = fallback ? ScrollMode.Enabled : ScrollMode.Disabled;
                PageScroll.VerticalScrollBarVisibility = fallback ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
                if (!fallback && PageScroll.VerticalOffset > 0) PageScroll.ChangeView(null, 0, null, true);
                int targetWidth = Math.Clamp(AppWindow.Size.Width, minimumWidthPx, work.Width);
                int targetHeight = Math.Clamp(AppWindow.Size.Height, minimumHeightPx, workHeight);
                if (targetWidth != AppWindow.Size.Width || targetHeight != AppWindow.Size.Height)
                    AppWindow.Resize(new SizeInt32(targetWidth, targetHeight));
                if (!Testing && (AppWindow.Position.X < work.Left || AppWindow.Position.Y < work.Top ||
                    AppWindow.Position.X + targetWidth > work.Right || AppWindow.Position.Y + targetHeight > work.Bottom))
                    AppWindow.Move(new PointInt32(Math.Clamp(AppWindow.Position.X, work.Left, work.Right - targetWidth),
                        Math.Clamp(AppWindow.Position.Y, work.Top, work.Bottom - targetHeight)));
            }
            finally { updatingConstraints = false; }
        }
        internal void ShowController()
        {
            if (closing) return;
            if (selection != null) { selection.Activate(); return; }
            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
            AppWindow.Show(); Activate(); MaintainOverlayOrder();
        }
        internal void HideToTray()
        {
            if (closing || exitRequested) return;
            AppWindow.Hide(); Shortcuts.IsExpanded = false;
            Editor?.Hide(); BorderEditor?.Hide(); Transfer?.Hide(); RenameEditor?.Hide();
            SaveSettings();
            ScheduleResourceCleanup();
        }
        private void ScheduleResourceCleanup()
        {
            if (closing || resourceCleanup == null) return;
            resourceCleanup.Stop(); resourceCleanup.Start();
        }
        internal void ReleaseIdleResources()
        {
            if (closing || selecting || HasSettingsDialog) return;
            // A full-desktop drawing/editor bitmap must not stay cached after its form closes.
            System.Drawing.BufferedGraphicsManager.Current.Invalidate();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: false);
            ResourceCleanupCount++;
        }
        internal void RequestExit()
        {
            if (exitRequested) return;
            exitRequested = true; Close();
        }
        private void QueueOverlayOrder()
        {
            if (closing || orderQueued) return;
            orderQueued = true;
            DispatcherQueue.TryEnqueue(() => { orderQueued = false; MaintainOverlayOrder(); });
        }
        internal bool MaintainOverlayOrder()
        {
            if (closing || selecting || Transfer != null || trayMenu.Visible || Masks.Values.Any(m => m.ContextMenuStrip.Visible)) return false;
            var group = new List<IntPtr>();
            group.AddRange(temporarySpotlights.Where(s => s.Visible).Select(s => s.Handle));
            group.AddRange(spotlights.Where(s => s.Visible).Select(s => s.Handle));
            group.AddRange(Zones.AsEnumerable().Reverse().Where(z => Masks[z].Visible).Select(z => Masks[z].Handle));
            return OverlayOrder.Ensure(group, Environment.ProcessId);
        }
        internal void BeginSelection(bool polygon) { BeginSelection(polygon ? SelectionShape.Polygon : SelectionShape.Rectangle); }
        internal void BeginTemporarySpotlight()
        {
            if (closing || selecting || HasSettingsDialog || temporarySpotlights.Count > 0) return;
            var cursor = Forms.Cursor.Position;
            var opening = FollowingOpening(new System.Drawing.Size(temporaryDiameter, temporaryDiameter), cursor, true);
            try
            {
                foreach (var screen in Forms.Screen.AllScreens)
                {
                    var bounds = screen.Bounds;
                    if (Testing) bounds.Offset(Forms.SystemInformation.VirtualScreen.Width + 5000, Forms.SystemInformation.VirtualScreen.Height + 5000);
                    temporarySpotlights.Add(new SpotlightForm(bounds, opening));
                }
                if (!Testing) temporaryWheel = new TemporarySpotlightWheel(delta => DispatcherQueue.TryEnqueue(() => ResizeTemporarySpotlight(delta)));
                if (!Testing && !temporaryWheel.Available) { warning = "临时聚光的滚轮调整不可用。"; }
                temporaryCursor = cursor; wheelRemainder = 0;
                RefreshMasks(); if (!Testing) temporarySpotlightTimer.Start();
            }
            catch { EndTemporarySpotlight(); throw; }
        }
        internal void ResizeTemporarySpotlight(int delta)
        {
            if (temporarySpotlights.Count == 0) return;
            wheelRemainder += delta;
            int steps = wheelRemainder / 120; wheelRemainder %= 120;
            if (steps == 0) return;
            temporaryDiameter = (int)Math.Clamp((long)temporaryDiameter + (long)steps * 30, 80, 2000);
            temporaryCursor = null; UpdateTemporarySpotlight();
        }
        private void UpdateTemporarySpotlight()
        {
            if (temporarySpotlights.Count == 0) return;
            var cursor = Forms.Cursor.Position;
            if (temporaryCursor.HasValue && temporaryCursor.Value == cursor) return;
            temporaryCursor = cursor;
            var opening = FollowingOpening(new System.Drawing.Size(temporaryDiameter, temporaryDiameter), cursor, true);
            foreach (var spotlight in temporarySpotlights) spotlight.UpdateOpening(opening);
        }
        internal void EndTemporarySpotlight()
        {
            temporarySpotlightTimer?.Stop(); temporaryWheel?.Dispose(); temporaryWheel = null; temporaryCursor = null;
            foreach (var spotlight in temporarySpotlights) spotlight.Dispose(); temporarySpotlights.Clear();
            if (!closing) RefreshMasks();
        }
        internal void ToggleSpotlight(bool follow = false, bool circle = false)
        {
            if (closing || selecting || HasSettingsDialog) return;
            if (spotlights.Count > 0 && spotlightFollow == follow && spotlightCircle == circle) { StopSpotlight(); return; }
            selecting = true; AppWindow.Hide(); RefreshMasks();
            SynchronizationContext previous = SynchronizationContext.Current;
            try
            {
                using (var picker = new SelectionForm(circle ? SelectionShape.Ellipse : SelectionShape.Rectangle, Forms.SystemInformation.VirtualScreen, circle))
                {
                    selection = picker;
                    if (picker.ShowDialog() == Forms.DialogResult.OK && picker.Result != null && !closing)
                    {
                        StopSpotlight(); spotlightFollow = follow; spotlightCircle = circle; spotlightSize = picker.Result.Bounds.Size; spotlightCursor = null;
                        var opening = follow ? FollowingOpening(spotlightSize, Forms.Cursor.Position, circle) : picker.Result;
                        foreach (var screen in Forms.Screen.AllScreens) spotlights.Add(new SpotlightForm(screen.Bounds, opening));
                        if (follow) spotlightTimer.Start();
                    }
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous); selection = null; selecting = false;
                if (!closing) { RefreshMasks(); ShowController(); }
                ScheduleResourceCleanup();
            }
        }
        internal void StopSpotlight()
        {
            spotlightTimer?.Stop(); spotlightFollow = false; spotlightCircle = false; spotlightCursor = null;
            foreach (var spotlight in spotlights) spotlight.Dispose(); spotlights.Clear();
            if (!closing) RefreshMasks();
        }
        internal static Zone FollowingOpening(System.Drawing.Size size, System.Drawing.Point cursor, bool circle = false)
        {
            var origin = new System.Drawing.Point(cursor.X - size.Width / 2, cursor.Y - size.Height / 2);
            var zone = Geometry.RectangleZone(origin, new System.Drawing.Point(origin.X + size.Width - 1, origin.Y + size.Height - 1), System.Drawing.Point.Empty);
            if (circle) zone.Curve = CurveShape.Ellipse;
            return zone;
        }
        private void UpdateFollowingSpotlight()
        {
            if (!spotlightFollow || selecting || Transfer != null || spotlights.Count == 0) return;
            var cursor = Forms.Cursor.Position;
            if (spotlightCursor.HasValue && spotlightCursor.Value == cursor) return;
            spotlightCursor = cursor; var opening = FollowingOpening(spotlightSize, cursor, spotlightCircle);
            foreach (var spotlight in spotlights) spotlight.UpdateOpening(opening);
        }
        internal void BeginSelection(SelectionShape shape)
        {
            if (closing || selecting || HasSettingsDialog) return;
            selecting = true; AppWindow.Hide(); RefreshMasks();
            SynchronizationContext previousContext = SynchronizationContext.Current;
            try
            {
                using (var picker = new SelectionForm(shape, Forms.SystemInformation.VirtualScreen))
                {
                    selection = picker;
                    if (picker.ShowDialog() == Forms.DialogResult.OK && picker.Result != null && !closing)
                    {
                        if (Zones.Count == 0) Master = true;
                        AddDrawnZone(picker.Result);
                    }
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                selection = null; selecting = false;
                if (!closing) { RefreshMasks(); ShowController(); }
                ScheduleResourceCleanup();
            }
        }
        internal void BeginGeometryEdit(Zone zone)
        {
            if (closing || selecting || HasSettingsDialog || !Zones.Contains(zone)) return;
            selecting = true; AppWindow.Hide(); RefreshMasks();
            SynchronizationContext previousContext = SynchronizationContext.Current;
            try
            {
                var screen = Forms.SystemInformation.VirtualScreen;
                var editBounds = Testing ? new System.Drawing.Rectangle(screen.Right + 500, screen.Bottom + 500, screen.Width, screen.Height) : screen;
                using (var editor = new ZoneEditForm(zone, editBounds))
                {
                    selection = editor;
                    if (editor.ShowDialog() == Forms.DialogResult.OK && editor.Result != null && !closing)
                        CommitGeometry(zone, editor.Result);
                }
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
                selection = null; selecting = false;
                if (!closing) { RefreshMasks(); ShowController(); }
                ScheduleResourceCleanup();
            }
        }
        internal bool CommitGeometry(Zone zone, Zone draft)
        {
            if (closing || !Masks.TryGetValue(zone, out MaskForm mask) || draft == null ||
                draft.Polygon != zone.Polygon || draft.Curve != zone.Curve || !Geometry.Valid(draft)) return false;
            zone.Points = draft.Points.ToList();
            zone.CornerPercent = Math.Clamp(draft.CornerPercent, 0, 50);
            mask.ApplyGeometry();
            RebuildCards(); RefreshMasks(); SaveSettings();
            return true;
        }
        internal void AddZone(Zone zone)
        {
            zone.Number = NextNumber; Zones.Add(zone);
            Masks.Add(zone, new MaskForm(zone, PassThrough.IsOn, () => SetEnabled(zone, false), ShowController, RequestExit));
            RebuildCards(); RefreshMasks(); SaveSettings();
        }
        internal void AddDrawnZone(Zone zone) { DefaultStyle.ApplyTo(zone); AddZone(zone); }
        internal int AddImportedZones(IList<Zone> values)
        {
            if (closing) return 0;
            if (values == null || values.Count == 0 || values.Any(z => !Geometry.Valid(z))) throw new FormatException("导入内容包含无效形状。");
            if (Zones.Count + values.Count > ShapeTextCodec.MaxShapes) throw new FormatException("导入后最多允许 100 个选区。");
            var pending = new Dictionary<Zone, MaskForm>();
            try
            {
                foreach (var value in values)
                {
                    var zone = new Zone { Polygon = value.Polygon, Curve = value.Curve, CornerPercent = value.CornerPercent, Points = value.Points.ToList() };
                    DefaultStyle.ApplyTo(zone);
                    pending.Add(zone, new MaskForm(zone, PassThrough.IsOn, () => SetEnabled(zone, false), ShowController, RequestExit));
                }
            }
            catch { foreach (var mask in pending.Values) mask.Dispose(); throw; }
            int nextNumber = NextNumber;
            if (Zones.Count == 0) Master = true;
            foreach (var entry in pending) { entry.Key.Number = nextNumber++; Zones.Add(entry.Key); Masks.Add(entry.Key, entry.Value); }
            RebuildCards(); RefreshMasks(); SaveSettings();
            return pending.Count;
        }
        internal async Task OpenTransferAsync(bool importing)
        {
            if (closing || selecting || HasSettingsDialog || (!importing && Zones.Count == 0)) return;
            var dialog = new TransferDialog(importing, Zones, Root.XamlRoot, Handle);
            Transfer = dialog; RefreshMasks();
            try
            {
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary && !closing)
                {
                    if (importing) AddImportedZones(dialog.ParsedZones);
                    else if (dialog.SavedPath != null) state.Text = "已导出 " + Zones.Count + " 个选区。";
                }
            }
            finally { dialog.Release(); Transfer = null; if (!closing) RefreshMasks(); ScheduleResourceCleanup(); }
        }
        internal void SetEnabled(Zone zone, bool value)
        {
            zone.Enabled = value;
            if (zoneSwitches.TryGetValue(zone, out ToggleSwitch toggle) && toggle.IsOn != value)
            {
                syncingSwitch = true;
                try { toggle.IsOn = value; } finally { syncingSwitch = false; }
            }
            RefreshMasks(); SaveSettings();
        }
        internal void RemoveZone(Zone zone)
        {
            if (Masks.Remove(zone, out MaskForm mask)) mask.Dispose(); Zones.Remove(zone);
            RebuildCards(); RefreshMasks(); SaveSettings();
        }
        internal void ClearZones()
        {
            foreach (MaskForm mask in Masks.Values) mask.Dispose(); Masks.Clear(); Zones.Clear();
            RebuildCards(); RefreshMasks(); SaveSettings();
        }
        internal void ToggleMaster() { if (closing) return; Master = !Master; RefreshMasks(); }
        internal void RefreshMasks()
        {
            if (closing) return;
            foreach (Zone zone in Zones)
            {
                Masks[zone].SetClickThrough(PassThrough.IsOn);
                Masks[zone].SetMaskVisible(Master && zone.Enabled && !selecting && Transfer == null && spotlights.Count == 0 && temporarySpotlights.Count == 0);
            }
            foreach (var spotlight in spotlights) { if (selecting || Transfer != null || temporarySpotlights.Count > 0) spotlight.Hide(); else if (!spotlight.Visible) spotlight.Show(); }
            foreach (var spotlight in temporarySpotlights) { if (!spotlight.Visible) spotlight.Show(); }
            spotlightMenu.Text = traySpotlight.Text = spotlights.Count > 0 && !spotlightFollow && !spotlightCircle ? "关闭矩形固定聚光灯" : "开启矩形固定聚光灯";
            followSpotlightMenu.Text = trayFollowSpotlight.Text = spotlights.Count > 0 && spotlightFollow && !spotlightCircle ? "关闭矩形跟随聚光灯" : "开启矩形跟随聚光灯";
            circleSpotlightMenu.Text = trayCircleSpotlight.Text = spotlights.Count > 0 && !spotlightFollow && spotlightCircle ? "关闭圆形固定聚光灯" : "开启圆形固定聚光灯";
            circleFollowSpotlightMenu.Text = trayCircleFollowSpotlight.Text = spotlights.Count > 0 && spotlightFollow && spotlightCircle ? "关闭圆形跟随聚光灯" : "开启圆形跟随聚光灯";
            UpdateState(); MaintainOverlayOrder();
        }
        internal async Task OpenAppearanceAsync(Zone zone)
        {
            if (closing || selecting || HasSettingsDialog || !Masks.TryGetValue(zone, out MaskForm mask)) return;
            var editor = new AppearanceEditor(zone, Root.XamlRoot, (rgb, percent) =>
            {
                if (closing || mask.IsDisposed) return;
                mask.ApplyAppearance(rgb, percent); UpdateState(); MaintainOverlayOrder();
            }, CreateDefaultZone());
            Editor = editor;
            try
            {
                ContentDialogResult result = await editor.ShowAsync();
                if (result == ContentDialogResult.Primary && !closing)
                {
                    zone.ColorRgb = editor.ColorRgb; zone.OpacityPercent = editor.OpacityPercent; SaveSettings();
                }
            }
            finally
            {
                editor.Release(); Editor = null;
                if (!closing && !mask.IsDisposed)
                {
                    mask.ApplyAppearance(zone.ColorRgb, zone.OpacityPercent); RebuildCards(); RefreshMasks();
                }
                ScheduleResourceCleanup();
            }
        }
        internal async Task OpenBorderAsync(Zone zone)
        {
            if (closing || selecting || HasSettingsDialog || !Masks.TryGetValue(zone, out MaskForm mask)) return;
            var editor = new BorderEditor(zone, Root.XamlRoot, value =>
            {
                if (closing || mask.IsDisposed) return;
                mask.ApplyBorder(value); MaintainOverlayOrder();
            }, CreateDefaultZone());
            BorderEditor = editor;
            try
            {
                if (await editor.ShowAsync() == ContentDialogResult.Primary && !closing)
                {
                    editor.Value.ApplyTo(zone); SaveSettings();
                }
            }
            finally
            {
                editor.Release(); BorderEditor = null;
                if (!closing && !mask.IsDisposed) { mask.ApplyBorder(BorderSettings.FromZone(zone)); RebuildCards(); RefreshMasks(); }
                ScheduleResourceCleanup();
            }
        }
        private Zone CreateDefaultZone() { var zone = new Zone(); DefaultStyle.ApplyTo(zone); return zone; }
        internal void UpdateDefaultStyle(ZoneStyle style)
        {
            DefaultStyle = style; SaveSettings();
            if (warning.Length == 0) state.Text = "已保存默认设置 · 用于之后框选、导入的新遮罩。";
        }
        internal async Task OpenDefaultAppearanceAsync()
        {
            if (closing || selecting || HasSettingsDialog) return;
            var draft = CreateDefaultZone();
            var editor = new AppearanceEditor(draft, Root.XamlRoot, (rgb, opacity) => { }, editingDefaults: true);
            Editor = editor;
            try
            {
                if (await editor.ShowAsync() == ContentDialogResult.Primary && !closing)
                {
                    draft.ColorRgb = editor.ColorRgb; draft.OpacityPercent = editor.OpacityPercent;
                    UpdateDefaultStyle(ZoneStyle.FromZone(draft));
                }
            }
            finally { editor.Release(); Editor = null; ScheduleResourceCleanup(); }
        }
        internal async Task OpenDefaultBorderAsync()
        {
            if (closing || selecting || HasSettingsDialog) return;
            var draft = CreateDefaultZone();
            var editor = new BorderEditor(draft, Root.XamlRoot, value => { }, editingDefaults: true);
            BorderEditor = editor;
            try
            {
                if (await editor.ShowAsync() == ContentDialogResult.Primary && !closing)
                {
                    editor.Value.ApplyTo(draft); UpdateDefaultStyle(ZoneStyle.FromZone(draft));
                }
            }
            finally { editor.Release(); BorderEditor = null; ScheduleResourceCleanup(); }
        }
        internal async Task OpenRenameAsync(Zone zone)
        {
            if (closing || selecting || HasSettingsDialog || !Zones.Contains(zone)) return;
            var editor = new RenameEditor(zone, Root.XamlRoot);
            RenameEditor = editor;
            try
            {
                if (await editor.ShowAsync() == ContentDialogResult.Primary && !closing && Zones.Contains(zone) && editor.IsValid)
                {
                    zone.Name = editor.Value;
                    double offset = CardScroll.VerticalOffset;
                    RebuildCards(); SaveSettings();
                    CardScroll.UpdateLayout(); CardScroll.ChangeView(null, offset, null, true);
                    if (warning.Length == 0) state.Text = "已重命名为“" + zone.DisplayName + "”。";
                }
            }
            finally { editor.Release(); RenameEditor = null; ScheduleResourceCleanup(); }
        }
        private void RebuildCards()
        {
            bool retired = false;
            var active = Zones.ToHashSet();
            foreach (Zone removed in cardCache.Keys.Where(z => !active.Contains(z)).ToArray())
            {
                cards.Children.Remove(cardCache[removed].Card); cardCache.Remove(removed);
                zoneSwitches.Remove(removed); pasteButtons.Remove(removed); retired = true;
            }
            if (Zones.Count == 0)
            {
                if (emptyCard == null)
                {
                    var empty = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    var icon = Ui.Icon("\uE710", 26); icon.Foreground = Ui.Brush(0x8291a5); empty.Children.Add(icon);
                    var label = Ui.Text("还没有遮罩选区", 15, 0x44536a); label.HorizontalAlignment = HorizontalAlignment.Center; empty.Children.Add(label);
                    empty.Children.Add(Ui.Text("点击上方按钮开始框选。", 12, 0x8291a5));
                    emptyCard = new Border { Child = empty, MinHeight = 160, Background = Ui.Brush(0xffffff), BorderBrush = Ui.Brush(0xe5eaf1), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20) };
                }
                if (!cards.Children.Contains(emptyCard)) cards.Children.Add(emptyCard);
            }
            else
            {
                if (emptyCard != null) { cards.Children.Remove(emptyCard); emptyCard = null; retired = true; }
                for (int i = 0; i < Zones.Count; i++)
                {
                    Zone zone = Zones[i];
                    var stamp = new CardStamp(zone.Number, zone.DisplayName, zone.Polygon, zone.Curve, zone.Points.Count, zone.Bounds, ZoneStyle.FromZone(zone));
                    if (!cardCache.TryGetValue(zone, out var entry) || entry.Stamp != stamp)
                    {
                        if (entry.Card != null) { cards.Children.Remove(entry.Card); retired = true; }
                        entry = (stamp, CreateZoneCard(zone, true)); cardCache[zone] = entry;
                    }
                    if (cards.Children.Count <= i || cards.Children[i] != entry.Card)
                    {
                        cards.Children.Remove(entry.Card); cards.Children.Insert(i, entry.Card);
                    }
                    if (zoneSwitches[zone].IsOn != zone.Enabled)
                    {
                        syncingSwitch = true; try { zoneSwitches[zone].IsOn = zone.Enabled; } finally { syncingSwitch = false; }
                    }
                }
            }
            UpdateState(); QueueLayoutConstraints();
            if (retired) ScheduleResourceCleanup();
        }
        private Border CreateZoneCard(Zone zone, bool interactive)
        {
            if (interactive) CardCreationCount++;
            var card = Ui.Columns(Ui.Star, Ui.Auto, Ui.Auto); card.RowSpacing = 8;
            for (int i = 0; i < 3; i++) card.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            var name = Ui.Text(zone.DisplayName, 15); name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            name.Name = interactive ? "ZoneName" + zone.Number : "ProbeName";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(name, zone.DisplayName + "，" + ShapeGeometry.Label(zone));
            name.IsDoubleTapEnabled = interactive;
            if (interactive)
            {
                ToolTipService.SetToolTip(name, "双击重命名 · 区域 " + zone.Number);
                name.DoubleTapped += async (s, e) => { e.Handled = true; await OpenRenameAsync(zone); };
            }
            var heading = Ui.Columns(Ui.Auto, Ui.Star); heading.ColumnSpacing = 8; heading.VerticalAlignment = VerticalAlignment.Center;
            Ui.Put(heading, Ui.ShapeIcon(zone), 0); Ui.Put(heading, name, 0, 1); Ui.Put(card, heading, 0);
            var enabled = new ToggleSwitch { IsOn = zone.Enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center, IsTabStop = interactive };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(enabled, "区域 " + zone.Number + " 的独立开关");
            if (interactive)
            {
                zoneSwitches[zone] = enabled;
                enabled.Toggled += (s, e) => { if (!syncingSwitch) SetEnabled(zone, enabled.IsOn); };
            }
            Ui.Put(card, enabled, 0, 1);
            var delete = new Button { Content = Ui.Icon("\uE74D", 14), Padding = new Thickness(9), VerticalAlignment = VerticalAlignment.Center, IsTabStop = interactive };
            if (interactive) delete.Click += (s, e) => RemoveZone(zone);
            ToolTipService.SetToolTip(delete, "删除这个选区");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(delete, "删除区域 " + zone.Number); Ui.Put(card, delete, 0, 2);
            var bounds = zone.Bounds;
            Ui.Put(card, Ui.Text(bounds.Width + " × " + bounds.Height + " px  ·  (" + bounds.X + ", " + bounds.Y + ")", 12, 0x657184), 1, 0, 3);

            var actions = Ui.Columns(Ui.Star, Ui.Auto); actions.RowSpacing = 8;
            actions.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto }); actions.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
            Button SettingButton(string title, string summary, int rgb)
            {
                var content = Ui.Columns(Ui.Auto, Ui.Star); content.ColumnSpacing = 8;
                Ui.Put(content, new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), Background = Ui.Brush((uint)rgb),
                    BorderBrush = Ui.Brush(0xc4cbd5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center }, 0);
                var labels = new StackPanel { Spacing = 2 }; labels.Children.Add(Ui.Text(title, 13)); labels.Children.Add(Ui.Text(summary, 12, 0x657184));
                Ui.Put(content, labels, 0, 1);
                return new Button { Content = content, Padding = new Thickness(10, 7, 10, 7), HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, IsTabStop = interactive };
            }
            var appearance = SettingButton("外观设置", "不透明度 " + zone.OpacityPercent + "%", Appearance.ValidRgb(zone.ColorRgb));
            appearance.Name = interactive ? "Appearance" + zone.Number : "ProbeAppearance";
            if (interactive) appearance.Click += async (s, e) => await OpenAppearanceAsync(zone);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(appearance, "区域 " + zone.Number + " 的外观设置");
            ToolTipService.SetToolTip(appearance, "#" + zone.ColorRgb.ToString("X6") + " · 不透明度 " + zone.OpacityPercent + "%");
            var borderValue = BorderSettings.FromZone(zone);
            var borderButton = SettingButton("边框设置", borderValue.Enabled ? "开启 · " + borderValue.Width + " px · " + BorderSettings.StyleNames[borderValue.Style] : "关闭", borderValue.ColorRgb);
            borderButton.Name = interactive ? "Border" + zone.Number : "ProbeBorder";
            if (interactive) borderButton.Click += async (s, e) => await OpenBorderAsync(zone);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(borderButton, "区域 " + zone.Number + " 的边框设置");
            ToolTipService.SetToolTip(borderButton, "设置这个选区的边框开关、粗细、颜色和线条样式");
            var edit = Ui.Button(zone.Curve == CurveShape.None ? "编辑顶点" : "编辑形状", () => { if (interactive) BeginGeometryEdit(zone); });
            edit.Name = interactive ? "Edit" + zone.Number : "ProbeEdit";
            edit.Padding = new Thickness(10, 7, 10, 7); edit.VerticalAlignment = VerticalAlignment.Stretch; edit.IsTabStop = interactive;
            ToolTipService.SetToolTip(edit, "拖动顶点调整形状，拖动内部或使用方向键整体移动");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edit, "编辑“" + zone.DisplayName + "”的" + (zone.Curve == CurveShape.None ? "顶点" : "形状"));
            var transfer = Ui.Columns(Ui.Star, Ui.Star); transfer.ColumnSpacing = 6;
            var copy = Ui.Button("复制", () => { if (interactive) CopyStyle(zone); });
            var paste = Ui.Button("粘贴", () => { if (interactive) PasteStyle(zone); });
            copy.Name = interactive ? "Copy" + zone.Number : "ProbeCopy"; paste.Name = interactive ? "Paste" + zone.Number : "ProbePaste";
            copy.Padding = paste.Padding = new Thickness(9, 7, 9, 7);
            copy.VerticalAlignment = paste.VerticalAlignment = VerticalAlignment.Stretch;
            copy.IsTabStop = paste.IsTabStop = interactive;
            paste.IsEnabled = copiedStyle.HasValue;
            ToolTipService.SetToolTip(copy, "复制此选区的外观及边框设置"); ToolTipService.SetToolTip(paste, "将已复制的外观及边框设置应用到此选区");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, "复制区域 " + zone.Number + " 的外观及边框设置");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(paste, "粘贴外观及边框设置到区域 " + zone.Number);
            if (interactive) pasteButtons[zone] = paste;
            Ui.Put(transfer, copy, 0); Ui.Put(transfer, paste, 0, 1);
            Ui.Put(actions, appearance, 0); Ui.Put(actions, edit, 0, 1); Ui.Put(actions, borderButton, 1); Ui.Put(actions, transfer, 1, 1);
            Ui.Put(card, actions, 2, 0, 3);
            return new Border { Name = interactive ? "ZoneCard" + zone.Number : "ProbeCard", Child = card, Background = Ui.Brush(0xffffff),
                BorderBrush = Ui.Brush(0xe5eaf1), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16) };
        }
        internal bool CopyStyle(Zone zone)
        {
            if (closing || selecting || HasSettingsDialog || !Zones.Contains(zone)) return false;
            copiedStyle = ZoneStyle.FromZone(zone);
            foreach (Button paste in pasteButtons.Values) paste.IsEnabled = true;
            state.Text = "已复制区域 " + zone.Number + " 的外观及边框设置。";
            return true;
        }
        internal bool PasteStyle(Zone zone)
        {
            if (closing || selecting || HasSettingsDialog || !copiedStyle.HasValue || !Masks.TryGetValue(zone, out MaskForm mask)) return false;
            copiedStyle.Value.ApplyTo(zone);
            mask.ApplyBorder(copiedStyle.Value.Border); mask.ApplyAppearance(zone.ColorRgb, zone.OpacityPercent);
            double offset = CardScroll.VerticalOffset;
            RebuildCards(); RefreshMasks(); SaveSettings();
            CardScroll.UpdateLayout(); CardScroll.ChangeView(null, offset, null, true);
            if (warning.Length == 0) state.Text = "已粘贴外观及边框设置到区域 " + zone.Number + "。";
            return true;
        }
        private void UpdateState()
        {
            int visible = Masks.Values.Count(m => m.Visible);
            string masterLabel = Master ? "关闭已选中的遮罩" : "打开已选中的遮罩";
            masterButton.Content = masterLabel;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(masterButton, masterLabel);
            masterButton.IsEnabled = undoButton.IsEnabled = clearButton.IsEnabled = Zones.Count > 0;
            exportButton.IsEnabled = Zones.Count > 0;
            trayToggle.Text = masterLabel; trayToggle.Enabled = Zones.Count > 0;
            count.Text = "选区  " + Zones.Count;
            state.Text = warning.Length > 0 ? warning : spotlights.Count > 0 ? (spotlightFollow ? "鼠标跟随聚光灯已开启" : "固定聚光灯已开启") + " · 在更多形状或托盘菜单中关闭" : Zones.Count == 0 ? "就绪 · 点击矩形或多边形按钮开始。" : visible > 0 ? "遮罩已开启 · " + visible + " 个区域显示中" : "遮罩已隐藏 · 已保留 " + Zones.Count + " 个选区";
            tray.Text = "屏幕遮罩 · " + visible + " / " + Zones.Count + " 个区域显示";
        }
        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                Settings settings;
                using (var stream = File.OpenRead(SettingsPath)) settings = (Settings)new XmlSerializer(typeof(Settings)).Deserialize(stream);
                DefaultStyle = ZoneStyle.FromZone((settings.DefaultStyle ?? new DefaultStyleSettings()).ToZone());
                PassThrough.IsOn = settings.ClickThrough;
                foreach (Zone zone in (settings.Zones ?? new List<Zone>()).Take(ShapeTextCodec.MaxShapes))
                {
                    if (!Geometry.Valid(zone)) continue;
                    if (zone.Number <= 0 || Zones.Any(z => z.Number == zone.Number)) zone.Number = NextNumber;
                    zone.ColorRgb = Appearance.ValidRgb(zone.ColorRgb); zone.OpacityPercent = Appearance.ValidOpacity(zone.OpacityPercent);
                    zone.CornerPercent = Math.Clamp(zone.CornerPercent, 0, 50);
                    BorderSettings.FromZone(zone).ApplyTo(zone);
                    Zones.Add(zone); Zone captured = zone;
                    Masks.Add(zone, new MaskForm(zone, settings.ClickThrough, () => SetEnabled(captured, false), ShowController, RequestExit));
                }
                Master = Zones.Count == 0;
            }
            catch { warning = "上次选区未能读取，可以重新框选。"; }
        }
        private void SaveSettings()
        {
            if (Testing) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)); string temp = SettingsPath + ".tmp";
                using (var stream = File.Create(temp)) new XmlSerializer(typeof(Settings)).Serialize(stream, new Settings { ClickThrough = PassThrough.IsOn,
                    DefaultStyle = DefaultStyleSettings.FromStyle(DefaultStyle), Zones = new List<Zone>(Zones) });
                if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, null); else File.Move(temp, SettingsPath);
            }
            catch { warning = "选区保存失败；本次仍可正常使用。"; UpdateState(); }
        }
        internal void Shutdown()
        {
            if (closing) return; closing = true;
            copiedStyle = null;
            keeper?.Stop(); resourceCleanup?.Stop(); orderWatcher?.Dispose(); Editor?.Hide(); BorderEditor?.Hide(); Transfer?.Hide(); RenameEditor?.Hide(); if (selection != null && !selection.IsDisposed) selection.Close();
            EndTemporarySpotlight(); StopSpotlight(); SaveSettings(); foreach (MaskForm mask in Masks.Values) mask.Dispose(); Masks.Clear();
            foreach (int id in hotkeys) Native.UnregisterHotKey(Handle, id); hotkeys.Clear();
            tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); DesktopInterop.RemoveWindowSubclass(Handle, subclass, new UIntPtr(1));
        }
    }
}
