using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace MarkApp
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await MediaPicker.PickPhotoAsync(new MediaPickerOptions
                {
                    Title = "Выберите фото"
                });

                if (result != null)
                {
                    // Открываем страницу редактирования
                    await Navigation.PushAsync(new EditPhotoPage(result));
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось выбрать фото: {ex.Message}", "OK");
            }
        }
    }
}