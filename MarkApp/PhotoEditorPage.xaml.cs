using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Point = Microsoft.Maui.Graphics.Point;
using Color = Microsoft.Maui.Graphics.Color;

namespace MarkApp
{
    public partial class PhotoEditorPage : ContentPage
    {
        // ��������� ���������
        private bool isMarkMode = false;
        private int currentMarkNumber = 0;
        private SKBitmap? originalImage;
        private SKBitmap? displayImage;

        // ��������������� � �����������
        private float scale = 1.0f;
        private float minScale = 0.5f;
        private float maxScale = 5.0f;
        private SKPoint offset = new SKPoint(0, 0);
        private SKPoint lastTouchPoint = new SKPoint(0, 0);
        private bool isDragging = false;

        // �������
        private class Marker
        {
            public SKPoint Position { get; set; }
            public int Number { get; set; }
        }

        private List<Marker> markers = new List<Marker>();

        public PhotoEditorPage()
        {
            InitializeComponent();
        }

        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                var result = await FilePicker.PickAsync(new PickOptions
                {
                    FileTypes = FilePickerFileType.Images
                });

                if (result == null)
                    return;

                using var stream = await result.OpenReadAsync();
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // ��������� ����������� � SkiaSharp
                originalImage = SKBitmap.Decode(memoryStream.ToArray());

                // ������� ����� ��� ����������� � �����������
                displayImage = originalImage.Copy();

                // ���������� ��������� ��������������� � �������
                scale = 1.0f;
                offset = new SKPoint(0, 0);
                markers.Clear();
                currentMarkNumber = 0;

                // ��������� ������
                canvasView.InvalidateSurface();
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� �����������: {ex.Message}", "OK");
            }
        }

        private void OnMarkModeClicked(object sender, EventArgs e)
        {
            if (originalImage == null)
            {
                DisplayAlert("��������������", "������� �������� �����������", "OK");
                return;
            }

            isMarkMode = !isMarkMode;
            markModeButton.Text = isMarkMode ? "��������� �����" : "����� �����";
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (markers.Count > 0)
            {
                markers.RemoveAt(markers.Count - 1);

                // ���� ������� ��������� �����, ��������� �������
                if (currentMarkNumber > 0)
                    currentMarkNumber--;

                // ��������������
                RedrawImage();
                canvasView.InvalidateSurface();
            }
        }

        private async void OnSavePhotoClicked(object sender, EventArgs e)
        {
            if (displayImage == null)
            {
                await DisplayAlert("��������������", "��� ����������� ��� ����������", "OK");
                return;
            }

            try
            {
                // �������� ���� ��� ����������
                string fileName = $"marked_photo_{DateTime.Now:yyyyMMdd_HHmmss}.jpg";
                string filePath = Path.Combine(FileSystem.CacheDirectory, fileName);

                // ��������� ����������� � ���������
                using (var outputStream = File.OpenWrite(filePath))
                {
                    // ������� ��������� ����������� � ������������ ������� � �������
                    using (var finalImage = new SKBitmap(originalImage.Width, originalImage.Height))
                    {
                        using (var canvas = new SKCanvas(finalImage))
                        {
                            // ������ ��������
                            canvas.DrawBitmap(originalImage, 0, 0);

                            // ������ ��� �������
                            DrawMarkersOnCanvas(canvas, 1.0f, new SKPoint(0, 0));
                        }

                        // ��������� � JPEG
                        finalImage.Encode(SKEncodedImageFormat.Jpeg, 95).SaveTo(outputStream);
                    }
                }

                // ���������� ��������� �� ������ � ������������ ����������
                bool share = await DisplayAlert("�������",
                    $"����������� ��������� �� ����:\n{filePath}",
                    "����������", "OK");

                if (share)
                {
                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "���������� ���� � �������",
                        File = new ShareFile(filePath)
                    });
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� �����������: {ex.Message}", "OK");
            }
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            SKCanvas canvas = e.Surface.Canvas;

            // ������� ������
            canvas.Clear(SKColors.LightGray);

            if (displayImage == null)
                return;

            // �������� ������� ������� � �����������
            var canvasSize = e.Info.Size;

            // ��������� ������� ��� ����������� �����������
            float imageAspect = (float)displayImage.Width / displayImage.Height;
            float canvasAspect = canvasSize.Width / canvasSize.Height;

            float scaleX, scaleY;
            SKRect destRect;

            if (imageAspect > canvasAspect)
            {
                // ����������� ���� �������, ������������ �� ������
                scaleX = scaleY = canvasSize.Width / displayImage.Width;
                float scaledHeight = displayImage.Height * scaleX;
                destRect = new SKRect(0, (canvasSize.Height - scaledHeight) / 2,
                                     canvasSize.Width, (canvasSize.Height + scaledHeight) / 2);
            }
            else
            {
                // ����������� ���� �������, ������������ �� ������
                scaleX = scaleY = canvasSize.Height / displayImage.Height;
                float scaledWidth = displayImage.Width * scaleY;
                destRect = new SKRect((canvasSize.Width - scaledWidth) / 2, 0,
                                     (canvasSize.Width + scaledWidth) / 2, canvasSize.Height);
            }

            // ��������� ������� � �������� �� ������������
            SKMatrix matrix = SKMatrix.CreateScale(scale, scale);
            SKPoint center = new SKPoint(canvasSize.Width / 2, canvasSize.Height / 2);
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(-center.X, -center.Y));
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(center.X + offset.X, center.Y + offset.Y));

            canvas.SetMatrix(matrix);

            // ������ �����������
            canvas.DrawBitmap(displayImage, destRect);

            // ������ �������
            DrawMarkersOnCanvas(canvas, scaleX, new SKPoint(destRect.Left, destRect.Top));

            // ���������� ������� �������������
            canvas.ResetMatrix();
        }

        private void DrawMarkersOnCanvas(SKCanvas canvas, float imageScale, SKPoint imageOffset)
        {
            if (markers.Count == 0)
                return;

            // ��������� ��� ��������
            float circleRadius = 15 / imageScale;

            // ����� ��� ���������
            using (var circlePaint = new SKPaint
            {
                Style = SKPaintStyle.Fill,
                Color = new SKColor(255, 0, 0, 180),
                IsAntialias = true
            })
            using (var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.White,
                StrokeWidth = 2 / imageScale,
                IsAntialias = true
            })
            using (var textPaint = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 12 / imageScale,
                IsAntialias = true,
                TextAlign = SKTextAlign.Center
            })
            {
                foreach (var marker in markers)
                {
                    // ������� ������� � ������ �������� �����������
                    SKPoint position = new SKPoint(
                        imageOffset.X + marker.Position.X,
                        imageOffset.Y + marker.Position.Y
                    );

                    // ������ ����
                    canvas.DrawCircle(position, circleRadius, circlePaint);
                    canvas.DrawCircle(position, circleRadius, strokePaint);

                    // ������ �����
                    canvas.DrawText(marker.Number.ToString(),
                                   position.X,
                                   position.Y + textPaint.TextSize / 3, // ������������ �� ���������
                                   textPaint);
                }
            }
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    // ���������� ����� �������
                    lastTouchPoint = e.Location;

                    if (isMarkMode && !e.InContact)
                    {
                        // ��������� ������ ������ ���� � ������ ����� � �� ������������ ������
                        AddMarker(e.Location);
                    }
                    else
                    {
                        // �������� �����������
                        isDragging = true;
                    }
                    break;

                case SKTouchAction.Moved:
                    if (isDragging)
                    {
                        // ����������� �����������
                        offset = new SKPoint(
                            offset.X + (e.Location.X - lastTouchPoint.X) / scale,
                            offset.Y + (e.Location.Y - lastTouchPoint.Y) / scale
                        );
                        lastTouchPoint = e.Location;
                        canvasView.InvalidateSurface();
                    }
                    break;

                case SKTouchAction.Released:
                    isDragging = false;
                    break;

                case SKTouchAction.WheelChanged:
                    // ��������������� ��� ��������� �������� ����
                    float newScale = scale * (float)Math.Pow(2, -e.WheelDelta / 10.0f);
                    scale = Math.Clamp(newScale, minScale, maxScale);
                    canvasView.InvalidateSurface();
                    break;
            }

            // �������� ������� ��� ������������
            e.Handled = true;
        }

        private void AddMarker(SKPoint touchPoint)
        {
            if (originalImage == null || displayImage == null)
                return;

            try
            {
                // �������� ������� ����������� �� ������
                var canvasSize = canvasView.CanvasSize;
                float imageAspect = (float)displayImage.Width / displayImage.Height;
                float canvasAspect = canvasSize.Width / canvasSize.Height;

                SKRect destRect;
                float scaleX, scaleY;

                if (imageAspect > canvasAspect)
                {
                    scaleX = scaleY = canvasSize.Width / displayImage.Width;
                    float scaledHeight = displayImage.Height * scaleX;
                    destRect = new SKRect(0, (canvasSize.Height - scaledHeight) / 2,
                                         canvasSize.Width, (canvasSize.Height + scaledHeight) / 2);
                }
                else
                {
                    scaleX = scaleY = canvasSize.Height / displayImage.Height;
                    float scaledWidth = displayImage.Width * scaleY;
                    destRect = new SKRect((canvasSize.Width - scaledWidth) / 2, 0,
                                         (canvasSize.Width + scaledWidth) / 2, canvasSize.Height);
                }

                // ��������� ������� ������� � �������� ������������
                SKMatrix matrix = SKMatrix.CreateScale(scale, scale);
                SKPoint center = new SKPoint(canvasSize.Width / 2, canvasSize.Height / 2);
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(-center.X, -center.Y));
                matrix = matrix.PostConcat(SKMatrix.CreateTranslation(center.X + offset.X, center.Y + offset.Y));

                // ����������� ������� ��� ��������� ��������� � �������� ������������
                SKMatrix inverseMatrix;
                if (!matrix.TryInvert(out inverseMatrix))
                    return;

                // ����������� ����� ������� � ���������� ������������� ������������
                SKPoint originalTouchPoint = inverseMatrix.MapPoint(touchPoint);

                // ���������, ��� ����� ��������� � �������� �����������
                if (!destRect.Contains(originalTouchPoint))
                    return;

                // ����������� ���������� � ���������� �����������
                float imageX = (originalTouchPoint.X - destRect.Left) / destRect.Width * displayImage.Width;
                float imageY = (originalTouchPoint.Y - destRect.Top) / destRect.Height * displayImage.Height;

                // ����������� ������� ��������
                currentMarkNumber++;

                // ������� ����� ������
                Marker newMarker = new Marker
                {
                    Position = new SKPoint(imageX, imageY),
                    Number = currentMarkNumber
                };

                // ��������� ������ � ������
                markers.Add(newMarker);

                // �������������� ����������� � ���������
                RedrawImage();
                canvasView.InvalidateSurface();
            }
            catch (Exception ex)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await DisplayAlert("������", $"�� ������� �������� �����: {ex.Message}", "OK");
                });
            }
        }

        private void RedrawImage()
        {
            if (originalImage == null)
                return;

            // ������� ����� ����� ������������� �����������
            displayImage = originalImage.Copy();
        }
    }
}