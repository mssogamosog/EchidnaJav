using EchidnaJav.Scraper.Services;

namespace EchidnaJav
{
    public partial class App : Application
    {
        private readonly ISilentWebViewSandbox _sandboxEngine;

        // 1. Inject the Sandbox Service into the App constructor
        public App(ISilentWebViewSandbox sandboxEngine)
        {
            InitializeComponent();
            _sandboxEngine = sandboxEngine;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // 2. Instantiate your primary UI Page
            var mainPage = new MainPage(_sandboxEngine);

            // 3. Command the sandbox to anchor itself into MainPage's underlying layout root
            // Ensure MainPage.xaml exposes its base Grid/Layout with x:Name="RootHostGrid"
            if (mainPage.FindByName<Layout>("RootHostGrid") is Layout rootLayout)
            {
                _sandboxEngine.AnchorToVisualTree(rootLayout);
            }

            // 4. Return the instantiated Window wrapper safely
            return new Window(mainPage) { Title = "EchidnaJav" };
        }
    }
}