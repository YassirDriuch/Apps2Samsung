using System.Text.Json;
using Apps2Samsung.Interfaces;
using Apps2Samsung.Models;
using Apps2Samsung.Samsung;
using Foundation;
using UIKit;
using WebKit;

namespace Apps2Samsung.Mobile.Services;

/// <summary>
/// iOS <see cref="ISamsungLoginService"/>: the Android recipe on WebKit. SignInGate runs in a
/// <see cref="WKWebView"/> shown as a sheet, and the shared <see cref="LoginCallbackListener"/>
/// catches the token it POSTs to the localhost redirect.
/// </summary>
public sealed class SamsungLoginService : ISamsungLoginService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<SamsungAuth> LoginAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

        // Listen before the page loads, so the redirect can't arrive first.
        _ = Task.Run(async () =>
        {
            try { tcs.TrySetResult(await LoginCallbackListener.WaitForTokenAsync(listening.Token)); }
            catch (OperationCanceledException) { tcs.TrySetCanceled(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });

        UINavigationController? sheet = null;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var presenter = Platform.GetCurrentUIViewController()
                    ?? throw new InvalidOperationException("No view controller to show the Samsung login from.");

                var login = new SamsungLoginViewController(
                    cancelled: () => tcs.TrySetCanceled(),
                    failed: ex => tcs.TrySetException(ex));
                sheet = new UINavigationController(login);
                // Swiping the sheet away is a cancel, the same as Android's back button.
                sheet.PresentationController!.Delegate = login.DismissWatcher;
                presenter.PresentViewController(sheet, true, null);
            });

            var tokenJson = await tcs.Task;

            return JsonSerializer.Deserialize<SamsungAuth>(tokenJson, JsonOptions)
                ?? throw new InvalidOperationException("Samsung login returned an unparseable token.");
        }
        finally
        {
            listening.Cancel();
            if (sheet is not null)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (sheet.PresentingViewController is not null)
                        sheet.DismissViewController(true, null);
                });
            }
        }
    }
}

/// <summary>Hosts SignInGate. Reports a cancel (button or swipe-down) or a main-frame load failure.</summary>
internal sealed class SamsungLoginViewController : UIViewController
{
    // An iOS Safari UA: Samsung's account pages treat an unknown embedded browser badly (Android
    // overrides its "; wv" WebView UA for the same reason).
    private const string SafariUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    private readonly Action _cancelled;
    private readonly Action<Exception> _failed;

    // WebKit and UIKit hold their delegates weakly; these fields keep them alive.
    private readonly NavigationDelegate _navigationDelegate;
    private readonly PopupDelegate _popupDelegate = new();
    internal DismissDelegate DismissWatcher { get; }

    private WKWebView? _web;

    public SamsungLoginViewController(Action cancelled, Action<Exception> failed)
    {
        _cancelled = cancelled;
        _failed = failed;
        _navigationDelegate = new NavigationDelegate(failed);
        DismissWatcher = new DismissDelegate(cancelled);
    }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();

        Title = "Samsung account";
        View!.BackgroundColor = UIColor.SystemBackground;
        NavigationItem.LeftBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Cancel, (_, _) => _cancelled());

        // The default (persistent) store, so the cross-domain login keeps its cookies.
        var config = new WKWebViewConfiguration { WebsiteDataStore = WKWebsiteDataStore.DefaultDataStore };
        config.Preferences.JavaScriptCanOpenWindowsAutomatically = true;

        _web = new WKWebView(View.Bounds, config)
        {
            AutoresizingMask = UIViewAutoresizing.FlexibleDimensions,
            CustomUserAgent = SafariUserAgent,
            NavigationDelegate = _navigationDelegate,
            UIDelegate = _popupDelegate,
        };
        View.AddSubview(_web);

        var url = SamsungOAuth.BuildAuthorizeUrl(LoginCallbackListener.RedirectUri);
        _web.LoadRequest(new NSUrlRequest(new NSUrl(url)));
    }

    // Surfaces main-frame load failures, so a failed page gives the real reason.
    private sealed class NavigationDelegate(Action<Exception> failed) : WKNavigationDelegate
    {
        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error)
            => Report(error);

        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error)
            => Report(error);

        private void Report(NSError error)
        {
            // A navigation replaced by another one (cancelled) or turned into a download / handled
            // elsewhere ("frame load interrupted") is not a failure.
            if (error.Domain == "NSURLErrorDomain" && error.Code == -999) // NSURLErrorCancelled
                return;
            if (error.Domain == "WebKitErrorDomain" && error.Code == 102)
                return;

            failed(new InvalidOperationException($"load error {error.Code}: {error.LocalizedDescription}"));
        }
    }

    // SignInGate opens some steps with window.open / target=_blank; keep them in this web view
    // (Android's SetSupportMultipleWindows does the same).
    private sealed class PopupDelegate : WKUIDelegate
    {
        public override WKWebView? CreateWebView(WKWebView webView, WKWebViewConfiguration configuration,
            WKNavigationAction navigationAction, WKWindowFeatures windowFeatures)
        {
            if (navigationAction.TargetFrame is null)
                webView.LoadRequest(navigationAction.Request);
            return null;
        }
    }

    internal sealed class DismissDelegate(Action cancelled) : UIAdaptivePresentationControllerDelegate
    {
        public override void DidDismiss(UIPresentationController presentationController) => cancelled();
    }
}
