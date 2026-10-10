namespace PermitPro.App.Services;

/// <summary>
/// Adds <c>'unsafe-eval'</c> to the Content Security Policy for this action only (see the CSP middleware in
/// <c>Program.cs</c>). Use it only for pages whose third-party scripts need eval, such as the Google Maps
/// JavaScript API, whose published CSP includes it.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowUnsafeEvalAttribute : Attribute
{
}
