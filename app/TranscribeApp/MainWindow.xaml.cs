using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace TranscribeApp;

/// <summary>
/// The application window: a NavigationView hosting the Live and Meetings pages.
/// Disposes the app-wide sidecar client (killing the sidecar) on close.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    public MainWindow()
    {
        InitializeComponent();
        AppLog.Write("TranscribeApp started");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ResizeToLogical(820, 900);

        Closed += OnClosed;
        ContentFrame.Navigate(typeof(LivePage));
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string;
        var target = tag == "meetings" ? typeof(MeetingsPage) : typeof(LivePage);
        if (ContentFrame.CurrentSourcePageType != target)
            ContentFrame.Navigate(target);
    }

    private void ResizeToLogical(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await App.Sidecar.DisposeAsync();
    }
}
