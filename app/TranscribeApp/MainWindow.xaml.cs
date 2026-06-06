using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TranscribeApp;

/// <summary>
/// The application window. Hosts a Frame that displays pages; owns the sidecar
/// client and kills the sidecar when the window closes.
/// </summary>
public sealed partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly SidecarClient _client = new();

    public MainWindow()
    {
        InitializeComponent();
        AppLog.Write("TranscribeApp started");

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");
        ResizeToLogical(660, 860);

        Closed += OnClosed;

        // Navigate the root frame to the main page, handing it the sidecar client.
        RootFrame.Navigate(typeof(MainPage), _client);
    }

    /// <summary>Resize to a logical (DPI-independent) size.</summary>
    private void ResizeToLogical(int width, int height)
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        await _client.DisposeAsync();
    }
}
