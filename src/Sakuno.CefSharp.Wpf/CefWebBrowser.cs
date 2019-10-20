using CefSharp;
using CefSharp.Internals;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace Sakuno.CefSharp.Wpf
{
    public class CefWebBrowser : HwndHost, IWebBrowserInternal
    {
        volatile int _isDisposed;
        public bool IsDisposed => _isDisposed == 1;

        IntPtr _childWindow;

        IBrowser? _browser;

        ManagedCefBrowserAdapter? _adapter;

        bool _isBrowserSettingCreatedByBrowser;
        BrowserSettings? _browserSettings;
        public BrowserSettings? BrowserSettings
        {
            get => _browserSettings;
            set
            {
                if (_browser != null)
                    throw new InvalidOperationException("Browser has been created. You cannot set BrowserSettings right now.");

                _browserSettings = value;
            }
        }

        public static readonly DependencyProperty AddressProperty =
            DependencyProperty.Register(nameof(Address), typeof(string), typeof(CefWebBrowser),
                new PropertyMetadata(string.Empty, OnAddressChanged));

        static void OnAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CefWebBrowser)d).OnAddressChanged((string)e.NewValue);
        void OnAddressChanged(string newValue)
        {
            if (_isAddressChanging)
                return;

            Load(newValue);
        }

        public string Address
        {
            get => (string)GetValue(AddressProperty);
            set => SetValue(AddressProperty, value);
        }

        public static readonly DependencyProperty CanGoBackProperty =
            DependencyProperty.Register(nameof(CanGoBack), typeof(bool), typeof(CefWebBrowser),
                new PropertyMetadata(false));
        public bool CanGoBack
        {
            get => (bool)GetValue(CanGoBackProperty);
            set => SetValue(CanGoBackProperty, value);
        }

        public static readonly DependencyProperty CanGoForwardProperty =
            DependencyProperty.Register(nameof(CanGoForward), typeof(bool), typeof(CefWebBrowser),
                new PropertyMetadata(false));
        public bool CanGoForward
        {
            get => (bool)GetValue(CanGoForwardProperty);
            set => SetValue(CanGoForwardProperty, value);
        }

        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(CefWebBrowser),
                new PropertyMetadata(false));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public IRequestContext? RequestContext { get; set; }

        public IJavascriptObjectRepository? JavascriptObjectRepository => _adapter?.JavascriptObjectRepository;

        public IDialogHandler? DialogHandler { get; set; }
        public IRequestHandler? RequestHandler { get; set; }
        public IDisplayHandler? DisplayHandler { get; set; }
        public ILoadHandler? LoadHandler { get; set; }
        public ILifeSpanHandler? LifeSpanHandler { get; set; }
        public IKeyboardHandler? KeyboardHandler { get; set; }
        public IJsDialogHandler? JsDialogHandler { get; set; }
        public IDragHandler? DragHandler { get; set; }
        public IDownloadHandler? DownloadHandler { get; set; }
        public IContextMenuHandler? MenuHandler { get; set; }
        public IFocusHandler? FocusHandler { get; set; }
        public IRenderProcessMessageHandler? RenderProcessMessageHandler { get; set; }
        public IFindHandler? FindHandler { get; set; }
        public IAudioHandler? AudioHandler { get; set; }
        public IResourceRequestHandlerFactory? ResourceRequestHandlerFactory { get; set; }

        public bool IsBrowserInitialized => _browser != null;

        public bool CanExecuteJavascriptInMainFrame { get; private set; }

        public string? TooltipText { get; private set; }

        IBrowserAdapter? IWebBrowserInternal.BrowserAdapter => _adapter;
        bool IWebBrowserInternal.HasParent { get; set; }

        bool _isAddressChanging;

        public event Action<IBrowser>? AfterBrowserCreated;

        public event EventHandler<ConsoleMessageEventArgs>? ConsoleMessage;
        public event EventHandler<StatusMessageEventArgs>? StatusMessage;
        public event EventHandler<FrameLoadStartEventArgs>? FrameLoadStart;
        public event EventHandler<FrameLoadEndEventArgs>? FrameLoadEnd;
        public event EventHandler<LoadErrorEventArgs>? LoadError;
        public event EventHandler<LoadingStateChangedEventArgs>? LoadingStateChanged;
        public event EventHandler<JavascriptMessageReceivedEventArgs>? JavascriptMessageReceived;

        public IBrowser? GetBrowser() => _browser;

        public void Load(string url)
        {
            if (_browser == null)
                return;

            using var frame = _browser.MainFrame;

            frame.LoadUrl(url);
        }

        public void Refresh() => _browser?.Reload();

        public void GoBack() => _browser?.GoBack();
        public void GoForward() => _browser?.GoForward();

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            Initialize();

            if (_childWindow == IntPtr.Zero)
                _childWindow = NativeMethods.CreateWindowExW(0, "Static", string.Empty,
                    NativeEnums.WindowStyles.WS_CHILD | NativeEnums.WindowStyles.WS_VISIBLE | NativeEnums.WindowStyles.WS_CLIPCHILDREN,
                    0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, Marshal.GetHINSTANCE(typeof(NativeMethods).Module), IntPtr.Zero);

            var windowInfo = new WindowInfo();
            windowInfo.SetAsChild(_childWindow);
            windowInfo.ExStyle |= (int)NativeEnums.ExtendedWindowStyles.WS_EX_NOACTIVATE;

            _adapter!.CreateBrowser(windowInfo, _browserSettings, (RequestContext?)RequestContext, null);

            if (_isBrowserSettingCreatedByBrowser)
            {
                _browserSettings!.Dispose();
                _browserSettings = null;
            }

            return new HandleRef(null, _childWindow);
        }

        protected override void DestroyWindowCore(HandleRef hwnd) => NativeMethods.DestroyWindow(hwnd.Handle);

        void Initialize()
        {
            if (_adapter != null)
                return;

            if (!Cef.IsInitialized)
                Cef.Initialize(new DefaultCefSettings());

            Cef.AddDisposable(this);

            if (_browserSettings == null)
            {
                _browserSettings = new BrowserSettings();
                _isBrowserSettingCreatedByBrowser = true;
            }

            _adapter = new ManagedCefBrowserAdapter(this, false);
        }

        protected override unsafe IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_WINDOWPOSCHANGED = 0x0047;

            if (msg == WM_WINDOWPOSCHANGED)
            {
                var info = (NativeStructs.WINDOWPOS*)lParam;

                _adapter!.Resize(info->cx, info->cy);
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        protected override void Dispose(bool disposing)
        {
            if (_isDisposed != 0 || Interlocked.CompareExchange(ref _isDisposed, 0, 1) != 0)
                return;

            Cef.RemoveDisposable(this);

            if (disposing)
            {
                _browser = null;

                _adapter?.Dispose();
                _adapter = null;
            }

            base.Dispose(disposing);
        }

        void InvokeOnUIThread(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.InvokeAsync(action);
        }

        void IWebBrowserInternal.OnAfterBrowserCreated(IBrowser browser)
        {
            _browser = browser;

            NativeMethods.GetWindowRect(_childWindow, out var rect);

            InvokeOnUIThread(() =>
            {
                if (!string.IsNullOrEmpty(Address))
                    Load(Address);
            });

            _adapter!.Resize(rect.Width, rect.Height);

            AfterBrowserCreated?.Invoke(_browser);
        }

        void IWebBrowserInternal.SetAddress(AddressChangedEventArgs args)
        {
            InvokeOnUIThread(() =>
            {
                _isAddressChanging = true;
                SetCurrentValue(AddressProperty, args.Address);
                _isAddressChanging = false;
            });
        }
        void IWebBrowserInternal.SetCanExecuteJavascriptOnMainFrame(bool canExecute) => CanExecuteJavascriptInMainFrame = canExecute;
        void IWebBrowserInternal.SetLoadingStateChange(LoadingStateChangedEventArgs args)
        {
            InvokeOnUIThread(() =>
            {
                SetCurrentValue(CanGoBackProperty, args.CanGoBack);
                SetCurrentValue(CanGoForwardProperty, args.CanGoForward);
                SetCurrentValue(IsLoadingProperty, args.IsLoading);

                LoadingStateChanged?.Invoke(this, args);
            });
        }
        void IWebBrowserInternal.SetTitle(TitleChangedEventArgs args) { }
        void IWebBrowserInternal.SetTooltipText(string tooltipText) => TooltipText = tooltipText;

        void IWebBrowserInternal.OnConsoleMessage(ConsoleMessageEventArgs args) => InvokeOnUIThread(() => ConsoleMessage?.Invoke(this, args));
        void IWebBrowserInternal.OnFrameLoadEnd(FrameLoadEndEventArgs args) => InvokeOnUIThread(() => FrameLoadEnd?.Invoke(this, args));
        void IWebBrowserInternal.OnFrameLoadStart(FrameLoadStartEventArgs args) => InvokeOnUIThread(() => FrameLoadStart?.Invoke(this, args));
        void IWebBrowserInternal.OnLoadError(LoadErrorEventArgs args) => InvokeOnUIThread(() => LoadError?.Invoke(this, args));
        void IWebBrowserInternal.OnStatusMessage(StatusMessageEventArgs args) => InvokeOnUIThread(() => StatusMessage?.Invoke(this, args));
        void IWebBrowserInternal.SetJavascriptMessageReceived(JavascriptMessageReceivedEventArgs args) => JavascriptMessageReceived?.Invoke(this, args);
    }
}
