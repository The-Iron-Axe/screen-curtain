using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ScreenCurtain
{
    internal static class Program
    {
        internal static string[] Arguments;
        internal static bool Testing { get { return Arguments != null && Arguments.Length == 2 &&
            (Arguments[0] == "--self-test" || Arguments[0] == "--self-test-numbering" || Arguments[0] == "--self-test-resources"); } }
        [STAThread]
        public static void Main(string[] args)
        {
            Arguments = args;
            if (Testing) File.WriteAllText(args[1], "START\n");
            Native.EnableDpi();
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            bool created;
            using (Mutex instance = new Mutex(true, Testing ? "Local\\ScreenCurtain.Test.WinUI" : "Local\\ScreenCurtain.Desktop.v1", out created))
            {
                if (!created) { Native.PostMessage(new IntPtr(0xffff), Native.ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                try
                {
                    WinRT.ComWrappersSupport.InitializeComWrappers();
                    Application.Start(p =>
                    {
                        SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                        new CurtainApp();
                    });
                }
                catch (Exception ex) { ReportError(ex); }
                finally { instance.ReleaseMutex(); }
            }
        }
        internal static void ReportError(Exception error)
        {
            string path = Testing ? Path.ChangeExtension(Arguments[1], ".error.txt") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenCurtain", "last-error.txt");
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, error.ToString()); } catch { }
            if (!Testing) System.Windows.Forms.MessageBox.Show("程序遇到问题，遮罩已关闭。\n\n" + error.Message, "屏幕遮罩", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
        }
    }
    public sealed partial class CurtainApp : Application
    {
        internal static MainWindow Main;
        public CurtainApp()
        {
            UnhandledException += (sender, ev) =>
            {
                ev.Handled = true;
                Main?.Shutdown();
                Program.ReportError(ev.Exception);
                Exit();
            };
            InitializeComponent();
        }
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            Main = new MainWindow(Program.Testing);
            Main.Activate();
        }
    }
}
