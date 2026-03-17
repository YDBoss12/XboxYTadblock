using System;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace XboxYouTube
{
    public sealed partial class MainPage : Page
    {
        private const string YouTubeTvUrl = "https://www.youtube.com/tv";

        private DispatcherTimer _gamepadTimer;
        private Gamepad _activeGamepad;
        private GamepadButtons _lastButtons = GamepadButtons.None;
        private DateTimeOffset _lastInput = DateTimeOffset.MinValue;
        private const int DebounceMs = 300;

        private const string AdBlockScript = @"
(function() {
    'use strict';

    // Hide cursor
    var style = document.createElement('style');
    style.textContent = '*, *::before, *::after { cursor: none !important; outline: none !important; }  :focus { outline: none !important; box-shadow: none !important; }';
    (document.head || document.documentElement).appendChild(style);

    function removeAds() {
        var selectors = [
            '.ad-showing', '.ytp-ad-module', '.ytp-ad-overlay-container',
            '.ytp-ad-text-overlay', '.ytp-ad-skip-button', '.ytp-ad-preview-container',
            '#player-ads', '#masthead-ad', '.ytd-banner-promo-renderer',
            '.ytd-ad-slot-renderer', 'ytd-promoted-video-renderer',
            'ytd-display-ad-renderer', '.video-ads', '.ytp-ad-image-overlay',
            'ytlr-ad-placement-renderer'
        ];
        selectors.forEach(function(s) {
            document.querySelectorAll(s).forEach(function(el) { el.remove(); });
        });
    }

    function skipAds() {
        var skip = document.querySelector('.ytp-ad-skip-button, .ytp-skip-ad-button, .ytp-ad-skip-button-modern');
        if (skip) { skip.click(); return; }
        var video = document.querySelector('video');
        if (video && (document.querySelector('.ad-showing') || document.querySelector('ytlr-ad-placement-renderer'))) {
            video.muted = true;
            if (video.duration && isFinite(video.duration)) video.currentTime = video.duration;
        }
    }

    var _fetch = window.fetch;
    window.fetch = function(url, opts) {
        if (typeof url === 'string' && (
            url.indexOf('doubleclick') >= 0 ||
            url.indexOf('googlesyndication') >= 0 ||
            url.indexOf('/pagead/') >= 0 ||
            url.indexOf('adservice.google') >= 0 ||
            url.indexOf('youtube.com/api/stats/ads') >= 0
        )) return Promise.resolve(new Response('{}', { status: 200 }));
        return _fetch.apply(this, arguments);
    };

    var _open = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {
        if (typeof url === 'string' && (
            url.indexOf('doubleclick') >= 0 ||
            url.indexOf('googlesyndication') >= 0 ||
            url.indexOf('/pagead/') >= 0
        )) url = 'about:blank';
        return _open.apply(this, arguments);
    };

    new MutationObserver(function() { removeAds(); skipAds(); })
        .observe(document.documentElement, { childList: true, subtree: true });

    setInterval(function() { removeAds(); skipAds(); }, 500);
    removeAds();

    // Remove forced dark mode
    var meta = document.querySelector('meta[name=""color-scheme""]');
    if (meta) meta.remove();
    document.documentElement.style.colorScheme = 'normal';
    document.documentElement.style.filter = 'none';
})();
";

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;
            Gamepad.GamepadAdded += (s, e) => _activeGamepad = e;
            Gamepad.GamepadRemoved += (s, e) => { if (_activeGamepad == e) _activeGamepad = null; };
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            YoutubeWebView.DefaultBackgroundColor = Windows.UI.Colors.White;
            YoutubeWebView.Navigate(new Uri(YouTubeTvUrl));
            _gamepadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _gamepadTimer.Tick += GamepadTimer_Tick;
            _gamepadTimer.Start();
            _ = ShowHintsThenFadeAsync();
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _gamepadTimer?.Stop();
        }

        private void YoutubeWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            var host = args.Uri?.Host ?? "";
            if (host.Contains("doubleclick.net") ||
                host.Contains("googlesyndication.com") ||
                host.Contains("googleadservices.com") ||
                host.Contains("adservice.google.com"))
            {
                args.Cancel = true;
                return;
            }
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void YoutubeWebView_ContentLoading(WebView sender, WebViewContentLoadingEventArgs args)
        {
            _ = InjectScriptAsync();
        }

        private async void YoutubeWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            await InjectScriptAsync();
            HideLoadingOverlay();
        }

        private void YoutubeWebView_ScriptNotify(object sender, NotifyEventArgs e) { }

        private async Task InjectScriptAsync()
        {
            try { await YoutubeWebView.InvokeScriptAsync("eval", new[] { AdBlockScript }); } catch { }
        }

        private async void GamepadTimer_Tick(object sender, object e)
        {
            if (_activeGamepad == null && Gamepad.Gamepads.Count > 0)
                _activeGamepad = Gamepad.Gamepads[0];
            if (_activeGamepad == null) return;

            var reading = _activeGamepad.GetCurrentReading();
            var pressed = reading.Buttons & ~_lastButtons;
            _lastButtons = reading.Buttons;
            if (pressed == GamepadButtons.None) return;

            var now = DateTimeOffset.UtcNow;
            if ((now - _lastInput).TotalMilliseconds < DebounceMs) return;
            _lastInput = now;

            if      ((pressed & GamepadButtons.DPadUp) != 0)        await DispatchKeyAsync("ArrowUp", "38");
            else if ((pressed & GamepadButtons.DPadDown) != 0)       await DispatchKeyAsync("ArrowDown", "40");
            else if ((pressed & GamepadButtons.DPadLeft) != 0)       await DispatchKeyAsync("ArrowLeft", "37");
            else if ((pressed & GamepadButtons.DPadRight) != 0)      await DispatchKeyAsync("ArrowRight", "39");
            else if ((pressed & GamepadButtons.A) != 0)              await DispatchKeyAsync("Enter", "13");
            else if ((pressed & GamepadButtons.B) != 0)              await DispatchKeyAsync("Escape", "27");
            else if ((pressed & GamepadButtons.X) != 0)              await DispatchKeyAsync(" ", "32");
            else if ((pressed & GamepadButtons.Y) != 0)              YoutubeWebView.Navigate(new Uri(YouTubeTvUrl));
            else if ((pressed & GamepadButtons.RightShoulder) != 0)  await DispatchKeyAsync("l", "76");
            else if ((pressed & GamepadButtons.LeftShoulder) != 0)   await DispatchKeyAsync("j", "74");
            else if ((pressed & GamepadButtons.Menu) != 0)           await DispatchKeyAsync("Escape", "27");
        }

        private async Task DispatchKeyAsync(string key, string keyCode)
        {
            string script = string.Format(@"
(function(){{
    var el = document.activeElement || document.body;
    var down = document.createEvent('KeyboardEvent');
    down.initKeyboardEvent('keydown', true, true, window, '{0}', 0, '', false, '');
    Object.defineProperty(down, 'keyCode', {{ get: function() {{ return {1}; }} }});
    Object.defineProperty(down, 'which',   {{ get: function() {{ return {1}; }} }});
    document.body.dispatchEvent(down);
    var up = document.createEvent('KeyboardEvent');
    up.initKeyboardEvent('keyup', true, true, window, '{0}', 0, '', false, '');
    Object.defineProperty(up, 'keyCode', {{ get: function() {{ return {1}; }} }});
    Object.defineProperty(up, 'which',   {{ get: function() {{ return {1}; }} }});
    document.body.dispatchEvent(up);
}})();", key.Replace("'", "\\'"), keyCode);
            try { await YoutubeWebView.InvokeScriptAsync("eval", new[] { script }); } catch { }
        }

        private void HideLoadingOverlay()
        {
            var anim = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromMilliseconds(400)) };
            Storyboard.SetTarget(anim, LoadingOverlay);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Completed += (s, ev) => LoadingOverlay.Visibility = Visibility.Collapsed;
            sb.Begin();
        }

        private async Task ShowHintsThenFadeAsync()
        {
            await Task.Delay(2000);
            ControllerHints.Opacity = 1;
            await Task.Delay(4000);
            var anim = new DoubleAnimation { From = 1, To = 0, Duration = new Duration(TimeSpan.FromSeconds(1)) };
            Storyboard.SetTarget(anim, ControllerHints);
            Storyboard.SetTargetProperty(anim, "Opacity");
            var sb = new Storyboard();
            sb.Children.Add(anim);
            sb.Begin();
        }
    }
}
