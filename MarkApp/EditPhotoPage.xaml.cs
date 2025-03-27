using Microsoft.Maui.Controls;
using Syncfusion.Maui.ImageEditor;

namespace MarkApp
{
    public partial class EditPhotoPage : ContentPage
    {
        public EditPhotoPage(FileResult photoFile)
        {
            InitializeComponent();
            LoadImage(photoFile);
        }

        private async void LoadImage(FileResult photoFile)
        {
            try
            {
                var stream = await photoFile.OpenReadAsync();
                PhotoEditor.Source = ImageSource.FromStream(() => stream);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось загрузить фото: {ex.Message}", "OK");
            }
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            try
            {
                // Получаем текущее изображение
                var imageStream = await PhotoEditor.GetImageStream();

                // Генерируем уникальное имя файла
                string fileName = $"edited_image_{DateTime.Now:yyyyMMdd_HHmmss}.png";

                // Сохраняем файл в галерею
                var path = Path.Combine(FileSystem.AppDataDirectory, fileName);

                using (var fileStream = File.Create(path))
                {
                    await imageStream.CopyToAsync(fileStream);
                }

                await DisplayAlert("Сохранение", "Изображение сохранено", "ОК");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Ошибка", $"Не удалось сохранить фото: {ex.Message}", "OK");
            }
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        private void OnMarkPointsClicked(object sender, EventArgs e)
        {
            // Переключение режима рисования
            //PhotoEditor.IsDrawingMode = !PhotoEditor.IsDrawingMode;
            
        }
    }
}