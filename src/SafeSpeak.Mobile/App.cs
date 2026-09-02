namespace SafeSpeak.Mobile;

public sealed class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(new MainPage()));
    }
}
