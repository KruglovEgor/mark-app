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
        private bool waitingForSecondTap = false;
        private Point firstTapLocation;

        // Держим размеры редактора и изображения для вычисления координат
        private double editorWidth, editorHeight;

        public EditPhotoPage()
        {
            InitializeComponent();

            // Подписываемся на событие загрузки изображения для получения его размеров
            PhotoEditor.Loaded += (s, e) => {
                editorWidth = PhotoEditor.Width;
                editorHeight = PhotoEditor.Height;
            };

            // Инициализация оверлея
            var tapGestureRecognizer = new TapGestureRecognizer();
            tapGestureRecognizer.Tapped += OnImageTapped;
            // TODO: разобраться почему при включении перестает работать выбор картинки
            //tapOverlay.GestureRecognizers.Add(tapGestureRecognizer);

            // Убедимся, что оверлей правильно реагирует на касания
            //tapOverlay.IsEnabled = true;
            //tapOverlay.InputTransparent = true; // По умолчанию касания проходят
        }
        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                FileTypes = FilePickerFileType.Images
            });

            if (result != null)
            {
                using var stream = await result.OpenReadAsync();
                
            }
        }
        private void OnMarkPointsClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            var btn = sender as Button;

            if (isMarkMode)
            {
                btn.Text = "Выключить режим меток";
                waitingForSecondTap = false;

                // Активируем оверлей для перехвата тапов
                //tapOverlay.InputTransparent = false;
            }
            else
            {
                btn.Text = "Включить режим меток";
                waitingForSecondTap = false;

                // Деактивируем оверлей, чтобы касания проходили к редактору
                //tapOverlay.InputTransparent = true;
            }
        }

        private void OnImageTapped(object sender, TappedEventArgs e)
        {
            if (!isMarkMode) return;

            // Получаем координаты касания относительно оверлея
            var point = e.GetPosition((View)sender);
            if (point == null) return;

            // Преобразуем координаты с учетом масштабирования и позиционирования изображения
            var imagePoint = CalculateImageCoordinates(point.Value);

            if (!waitingForSecondTap)
            {
                // Первое касание - добавляем круг
                firstTapLocation = imagePoint;
                AddCircleAt(imagePoint);
                waitingForSecondTap = true;
            }
            else
            {
                // Второе касание - добавляем текст
                AddLabelAt(imagePoint);
                waitingForSecondTap = false;
            }
        }

        private Point CalculateImageCoordinates(Point tapPoint)
        {
            double relativeX = tapPoint.X / PhotoEditor.Width;
            double relativeY = tapPoint.Y / PhotoEditor.Height;
            return new Point(relativeX * PhotoEditor.Width, relativeY * PhotoEditor.Height);
        }

        private void AddCircleAt(Point point)
        {
            try
            {
                var circleSettings = new ImageEditorShapeSettings
                {
                    Color = Colors.Red,
                    StrokeThickness = 3,
                    IsFilled = true,
                    Bounds = new Rect(point.X - 10, point.Y - 10, 20, 20)
                };

                PhotoEditor.AddShape(AnnotationShape.Circle, circleSettings);
            }
            catch (Exception ex)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Ошибка", $"Не удалось добавить круг: {ex.Message}", "OK");
                });
            }
        }

        private void AddLabelAt(Point point)
        {
            try
            {
                currentMarkNumber++;

                var textSettings = new ImageEditorTextSettings
                {
                    TextAlignment = TextAlignment.Center,
                    Background = Colors.Black,
                    Bounds = new Rect(point.X - 12, point.Y - 12, 24, 24)
                };

                PhotoEditor.AddText(currentMarkNumber.ToString(), textSettings);

                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Успешно", $"Метка #{currentMarkNumber} добавлена", "OK");
                });
            }
            catch (Exception ex)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("Ошибка", $"Не удалось добавить текст: {ex.Message}", "OK");
                });
            }
        }
    }
}