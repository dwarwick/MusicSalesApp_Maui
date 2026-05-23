using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Interoperability",
    "CA1422:Validate platform compatibility",
    Justification = "The installed .NET iOS SDK does not expose StoreKit 2 bindings yet; the app must use StoreKit 1 on this toolchain.",
    Scope = "type",
    Target = "~T:MusicSalesApp.Maui.Platforms.iOS.AppStoreBillingService")]

[assembly: SuppressMessage(
    "Interoperability",
    "CA1422:Validate platform compatibility",
    Justification = "The installed .NET iOS SDK does not expose StoreKit 2 bindings yet; the app must use StoreKit 1 on this toolchain.",
    Scope = "type",
    Target = "~T:MusicSalesApp.Maui.Platforms.iOS.AppStoreBillingService+ProductRequestDelegate")]