using Microsoft.Maui.Controls;

namespace MarkApp
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnNavigateToEditPageClicked(object sender, EventArgs e)
        {
            // Просто переходим на страницу редактора
            await Navigation.PushAsync(new PhotoEditorPage());
        }
    }
}