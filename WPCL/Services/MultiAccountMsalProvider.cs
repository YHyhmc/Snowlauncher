using Microsoft.Identity.Client;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Authenticators;
using XboxAuthNet.Game.Msal;

namespace WPCL.Services;

public sealed class MultiAccountMsalProvider : IAuthenticationProvider
{
    private readonly MsalOAuthBuilder _builder;

    public MultiAccountMsalProvider(IPublicClientApplication application)
    {
        _builder = new MsalOAuthBuilder(application);
    }

    public IAuthenticator Authenticate() => _builder.CodeFlow();

    public IAuthenticator AuthenticateInteractively() =>
        _builder.Interactive(builder => builder
            .WithPrompt(Prompt.SelectAccount)
            .WithUseEmbeddedWebView(false)
            .WithSystemWebViewOptions(new SystemWebViewOptions
            {
                HtmlMessageSuccess = OAuthCompletionPage.Success,
                HtmlMessageError = OAuthCompletionPage.Error
            }));

    public IAuthenticator AuthenticateSilently() => _builder.Silent();

    public ISessionValidator CreateSessionValidator() => StaticValidator.Invalid;

    public IAuthenticator ClearSession() => _builder.ClearSession();

    public IAuthenticator Signout() => _builder.ClearSession();
}
