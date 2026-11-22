using System.Windows;
using System.Windows.Input;

using Bloxstrap.UI.ViewModels.Settings;
using Wpf.Ui.Mvvm.Contracts;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for FastFlagsPage.xaml
    /// </summary>
    public partial class FastFlagsPage
    {
        private bool _initialLoad = false;

        private FastFlagsViewModel _viewModel = null!;

        public FastFlagsPage()
        {
            SetupViewModel();
            InitializeComponent();

            FlagTemplateManager.EnsureDefaults();
            LoadTemplates();
        }

        #region Xyllostrap templates
        private void LoadTemplates()
        {
            var templates = FlagTemplateManager.GetAll();
            TemplatesList.ItemsSource = templates;
            NoTemplatesText.Visibility = templates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static FlagTemplate? GetTemplate(object sender) => (sender as FrameworkElement)?.Tag as FlagTemplate;

        private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplate(sender) is not FlagTemplate template)
                return;

            FlagTemplateManager.Apply(template);
            SetupViewModel();

            Frontend.ShowMessageBox($"Applied \"{template.Name}\" ({template.Flags.Count} flags).\n\nPress Save at the bottom of the window to keep the changes.", MessageBoxImage.Information);
        }

        private void RemoveTemplateFlags_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplate(sender) is not FlagTemplate template)
                return;

            FlagTemplateManager.Remove(template);
            SetupViewModel();

            Frontend.ShowMessageBox($"Removed the flags from \"{template.Name}\".\n\nPress Save at the bottom of the window to keep the changes.", MessageBoxImage.Information);
        }

        private void EditTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplate(sender) is not FlagTemplate template || !File.Exists(template.FilePath))
                return;

            Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = $"\"{template.FilePath}\"", UseShellExecute = true });
        }

        private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (GetTemplate(sender) is not FlagTemplate template)
                return;

            var result = Frontend.ShowMessageBox($"Delete the template \"{template.Name}\"?\n\nThis only deletes the template, not the flags you have already applied.", MessageBoxImage.Question, MessageBoxButton.YesNo);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                FlagTemplateManager.Delete(template);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("FastFlagsPage::DeleteTemplate", ex);
                Frontend.ShowMessageBox($"Could not delete the template: {ex.Message}", MessageBoxImage.Error);
            }

            LoadTemplates();
        }

        private void CreateTemplate_Click(object sender, RoutedEventArgs e)
        {
            string name = NewTemplateName.Text.Trim();

            if (String.IsNullOrEmpty(name))
            {
                Frontend.ShowMessageBox("Type a name for the new template first.", MessageBoxImage.Warning);
                return;
            }

            if (App.FastFlags.Prop.Count == 0)
            {
                Frontend.ShowMessageBox("You don't have any FastFlags set yet. Add some with the presets or the FastFlag editor, then save them as a template.", MessageBoxImage.Warning);
                return;
            }

            var template = FlagTemplateManager.FromCurrentFlags(name);

            if (File.Exists(Path.Combine(FlagTemplateManager.Folder, String.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim() + ".json"))
                && Frontend.ShowMessageBox($"A template called \"{name}\" already exists. Replace it?", MessageBoxImage.Question, MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            FlagTemplateManager.Save(template);
            NewTemplateName.Text = "";
            LoadTemplates();
        }

        private void OpenTemplatesFolder_Click(object sender, RoutedEventArgs e)
        {
            Directory.CreateDirectory(FlagTemplateManager.Folder);
            Process.Start(new ProcessStartInfo { FileName = FlagTemplateManager.Folder, UseShellExecute = true });
        }

        private void RestoreTemplates_Click(object sender, RoutedEventArgs e)
        {
            FlagTemplateManager.RestoreDefaults();
            LoadTemplates();
        }
        #endregion

        private void SetupViewModel()
        {
            _viewModel = new FastFlagsViewModel();

            _viewModel.OpenFlagEditorEvent += OpenFlagEditor;
            _viewModel.RequestPageReloadEvent += (_, _) => SetupViewModel();

            DataContext = _viewModel;
        }

        private void OpenFlagEditor(object? sender, EventArgs e)
        {
            if (Window.GetWindow(this) is INavigationWindow window)
            {
               window.Navigate(typeof(FastFlagEditorPage));
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // refresh datacontext on page load to synchronize with editor page
            
            if (!_initialLoad)
            {
                _initialLoad = true;
                return;
            }

            SetupViewModel();
            LoadTemplates();
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });

            e.Handled = true;
        }

        private void ValidateInt32(object sender, TextCompositionEventArgs e) => e.Handled = e.Text != "-" && !Int32.TryParse(e.Text, out int _);
        
        private void ValidateUInt32(object sender, TextCompositionEventArgs e) => e.Handled = !UInt32.TryParse(e.Text, out uint _);
    }
}
