using Microsoft.Maui.Controls;
using Syncfusion.Maui.Core;
using Syncfusion.Maui.ImageEditor;

namespace MarkApp
{
    public partial class EditPhotoPage : ContentPage
    {
        private bool isCircleMode = false;

        public EditPhotoPage(FileResult photoFile)
        {
            InitializeComponent();
            LoadImage(photoFile);

            // Configure the toolbar
            ConfigureImageEditor();
        }

        private void ConfigureImageEditor()
        {
            // Toolbar configuration
            if (PhotoEditor.ToolbarSettings != null)
            {
                // Make sure shapes are visible in the toolbar
                //PhotoEditor.ToolbarSettings.ToolbarItemVisibility.Shape = true;
            }
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
                await Navigation.PopAsync();
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
            isCircleMode = !isCircleMode;
            var btn = sender as Button;

            if (isCircleMode)
            {
                btn.Text = "Выключить режим меток";

                // Add a circle directly
                AddCircle();

                // Show the toolbar to edit shapes
                if (PhotoEditor.ToolbarSettings != null)
                {
                    //PhotoEditor.ToolbarSettings.IsVisible = true;
                }
            }
            else
            {
                btn.Text = "Добавить метку";

                // Hide the toolbar
                if (PhotoEditor.ToolbarSettings != null)
                {
                    //PhotoEditor.ToolbarSettings.IsVisible = false;
                }
            }
        }

        private void AddCircle()
        {
            try
            {
                // This is the method that should work with Syncfusion SfImageEditor
                // Get the center point of the editor
                double centerX = PhotoEditor.Width / 2;
                double centerY = PhotoEditor.Height / 2;

                // Create circle shape settings
                ImageEditorShapeSettings circleSettings = new ImageEditorShapeSettings
                {
                    Color = Colors.Red,
                    StrokeThickness = 3,
                    IsFilled = true,
                };

                // Add the circle shape at the center
                PhotoEditor.AddShape(AnnotationShape.Circle, circleSettings);

                // Display success message
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Успех", "Метка добавлена. Используйте инструменты редактора для перемещения и изменения размера.", "OK");
                });
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Ошибка", $"Не удалось добавить метку: {ex.Message}", "OK");
                });
            }
        }
    }
}