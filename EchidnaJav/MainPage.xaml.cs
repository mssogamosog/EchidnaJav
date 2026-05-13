using EchidnaJav.Scraper.Services;

namespace EchidnaJav
{
    public partial class MainPage : ContentPage
    {
        public MainPage(ISilentWebViewSandbox sandboxEngine)
        {
            InitializeComponent();
            sandboxEngine.AnchorToVisualTree(RootHostGrid);
        }
    }
}
