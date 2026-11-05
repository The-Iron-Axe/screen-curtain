using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ScreenCurtain
{
    internal sealed class RenameEditor
    {
        internal const int MaxNameLength = 40;
        internal readonly ContentDialog Dialog;
        internal readonly TextBox Input;
        internal string Value { get { return Input.Text.Trim(); } }
        internal bool IsValid { get { return Value.Length > 0 && Value.Length <= MaxNameLength; } }
        private ContentDialogResult? testResult;

        internal RenameEditor(Zone zone, XamlRoot root)
        {
            Input = new TextBox { Header = "遮罩名称", Text = zone.DisplayName, MaxLength = MaxNameLength,
                PlaceholderText = "输入名称，最多 40 个字符", AcceptsReturn = false, MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Stretch };
            Dialog = new ContentDialog { Title = "重命名遮罩", Content = Input, PrimaryButtonText = "确定", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary, XamlRoot = root, RequestedTheme = ElementTheme.Light };
            Input.TextChanged += OnTextChanged;
            Dialog.PrimaryButtonClick += OnPrimaryClick;
            Dialog.Opened += OnOpened;
        }
        private void OnTextChanged(object sender, TextChangedEventArgs e) { Dialog.IsPrimaryButtonEnabled = IsValid; }
        private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs e) { e.Cancel = !IsValid; }
        private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs e) { Input.Focus(FocusState.Programmatic); Input.SelectAll(); }
        internal void Release()
        {
            Input.TextChanged -= OnTextChanged; Dialog.PrimaryButtonClick -= OnPrimaryClick; Dialog.Opened -= OnOpened;
            Dialog.Content = null;
        }
        internal async Task<ContentDialogResult> ShowAsync() { var result = await Dialog.ShowAsync(); return testResult ?? result; }
        internal void Hide() { Dialog.Hide(); }
        internal void CompleteForTesting(bool confirm)
        {
            if (!Program.Testing || (confirm && !IsValid)) throw new InvalidOperationException();
            testResult = confirm ? ContentDialogResult.Primary : ContentDialogResult.None; Dialog.Hide();
        }
    }
}
