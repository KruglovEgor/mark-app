using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Syncfusion.Maui.ImageEditor;
using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Maui;

namespace MarkApp
{
    public partial class EditPhotoPage : ContentPage
    {
        private bool isMarkMode = false;
        private int currentMarkNumber = 0;
        private Dictionary<int, int> markCircles = new Dictionary<int, int>();
        private Dictionary<int, int> markLabels = new Dictionary<int, int>();

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
                var path = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, fileName);

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
            isMarkMode = !isMarkMode;
            var btn = sender as Button;

            if (isMarkMode)
            {
                btn.Text = "Выключить режим меток";

                // Добавляем метку по центру
                double centerX = PhotoEditor.Width / 2;
                double centerY = PhotoEditor.Height / 2;
                AddMarkAt(centerX, centerY);
            }
            else
            {
                btn.Text = "Включить режим меток";
            }
        }

        // Метод для добавления метки в указанную позицию
        private void AddMarkAt(double x, double y)
        {
            try
            {
                // Увеличиваем счетчик меток
                currentMarkNumber++;

                // 1. Добавляем круг (точку)
                // Используем доступные методы API Syncfusion
                ImageEditorShapeSettings circleSettings = new ImageEditorShapeSettings
                {
                    Color = Colors.Red,
                    StrokeThickness = 3,
                    IsFilled = true,
                };
                PhotoEditor.AddShape(AnnotationShape.Circle, circleSettings);

                // 2. Добавляем метку с номером
                PhotoEditor.AddText(currentMarkNumber.ToString());

                // Показываем сообщение
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Успешно", $"Добавлена метка #{currentMarkNumber}. Используйте инструменты редактора для изменения положения и размера.", "OK");
                });
            }
            catch (Exception ex)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Ошибка", $"Не удалось добавить метку: {ex.Message}", "OK");
                });
            }
        }

        // Дополнительный метод для добавления метки по кнопке
        private void OnAddMarkClicked(object sender, EventArgs e)
        {
            // Добавляем новую метку по центру экрана
            double centerX = PhotoEditor.Width / 2;
            double centerY = PhotoEditor.Height / 2;
            AddMarkAt(centerX, centerY);
        }
    }
}