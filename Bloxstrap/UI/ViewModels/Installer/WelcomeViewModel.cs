namespace Bloxstrap.UI.ViewModels.Installer
{
    public class WelcomeViewModel : NotifyPropertyChangedViewModel
    {
        public string MainText =>
            "Thank you for downloading Xyllostrap.\n\n" +
            "The only official place to download Xyllostrap from is [github.com/06Xyllo/Xyllostrap](https://github.com/06Xyllo/Xyllostrap), so please verify that you got it from there.\n\n" +
            "This installation process will be quick and simple, and you will be able to configure any of Xyllostrap's settings after installation.";

        public bool CanContinue { get; set; } = false;
    }
}
