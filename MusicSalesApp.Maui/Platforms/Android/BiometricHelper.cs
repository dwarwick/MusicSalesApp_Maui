using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Java.Util.Concurrent;

namespace MusicSalesApp.Maui.Platforms.Android;

public static class BiometricHelper
{
    public static Task<(bool Success, string Error)> AuthenticateAsync()
    {
        var tcs = new TaskCompletionSource<(bool, string)>();

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null)
        {
            tcs.SetResult((false, "Cannot access current activity."));
            return tcs.Task;
        }

        var biometricManager = BiometricManager.From(activity);
        int canAuthenticate = biometricManager.CanAuthenticate(BiometricManager.Authenticators.BiometricStrong
            | BiometricManager.Authenticators.BiometricWeak);

        if (canAuthenticate != BiometricManager.BiometricSuccess)
        {
            tcs.SetResult((false, "Biometric authentication is not available on this device."));
            return tcs.Task;
        }

        var executor = ContextCompat.GetMainExecutor(activity);
        var callback = new BiometricCallback(tcs);

        var promptInfo = new BiometricPrompt.PromptInfo.Builder()
            .SetTitle("Biometric Login")
            .SetSubtitle("Authenticate to sign in to StreamTunes")
            .SetNegativeButtonText("Cancel")
            .SetAllowedAuthenticators(BiometricManager.Authenticators.BiometricStrong
                | BiometricManager.Authenticators.BiometricWeak)
            .Build();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (activity is FragmentActivity fragmentActivity)
                {
                    var biometricPrompt = new BiometricPrompt(fragmentActivity, executor!, callback);
                    biometricPrompt.Authenticate(promptInfo);
                }
                else
                {
                    tcs.TrySetResult((false, "Activity does not support biometric prompt."));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult((false, $"Biometric error: {ex.Message}"));
            }
        });

        return tcs.Task;
    }

    private sealed class BiometricCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<(bool, string)> _tcs;

        public BiometricCallback(TaskCompletionSource<(bool, string)> tcs) => _tcs = tcs;

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
        {
            base.OnAuthenticationSucceeded(result);
            _tcs.TrySetResult((true, string.Empty));
        }

        public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            _tcs.TrySetResult((false, errString?.ToString() ?? "Authentication cancelled."));
        }

        public override void OnAuthenticationFailed()
        {
            base.OnAuthenticationFailed();
            // Don't complete yet — the system will show an error and let the user retry
        }
    }
}
