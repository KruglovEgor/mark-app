using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MarkApp
{
    public partial class PhotoEditorPage : ContentPage
    {
        private SKBitmap loadedImage;
        private SKMatrix matrix = SKMatrix.CreateIdentity();
        private bool isMarkMode = false;
        private List<SKPoint> markPoints = new List<SKPoint>();
        private List<List<SKPoint>> allMarks = new List<List<SKPoint>>();
        private List<SKPoint> currentMark = new List<SKPoint>();

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
                    FileTypes = FilePickerFileType.Images,
                    PickerTitle = "�������� ����"
                });

                if (result != null)
                {
                    using (var stream = await result.OpenReadAsync())
                    {
                        // �������� ����������� � SKBitmap
                        loadedImage = SKBitmap.Decode(stream);

                        // �������� ����� ��� �������� ������ �����������
                        markPoints = new List<SKPoint>();
                        allMarks = new List<List<SKPoint>>();
                        currentMark = new List<SKPoint>();

                        // ���������� ��������� ������� ��� ��������������� �����������, ����� ��� ��������� ������������
                        CalculateImageMatrix();

                        // ������� ����������� �������
                        canvasView.InvalidateSurface();
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� �����������: {ex.Message}", "OK");
            }
        }

        private void CalculateImageMatrix()
        {
            if (loadedImage == null) return;

            // ���������� ����������� ������ ������ � �����������
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float imageWidth = loadedImage.Width;
            float imageHeight = loadedImage.Height;

            // ������������ ��������������� ��� �������� ����������� ��� �����
            float scaleX = canvasWidth / imageWidth;
            float scaleY = canvasHeight / imageHeight;
            float scale = Math.Min(scaleX, scaleY);

            // ������������� �����������
            float translateX = (canvasWidth - (imageWidth * scale)) / 2;
            float translateY = (canvasHeight - (imageHeight * scale)) / 2;

            // �������� ������� �������������
            matrix = SKMatrix.CreateScale(scale, scale);
            matrix = matrix.PostConcat(SKMatrix.CreateTranslation(translateX, translateY));
        }

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            if (loadedImage != null)
            {
                // �������� ������������� �������
                if (matrix.IsIdentity)
                {
                    CalculateImageMatrix();
                }

                // ��������� ������� ��������� �������
                canvas.Save();
                // ��������� ������� ������������� � �������
                canvas.SetMatrix(matrix);
                // ��������� ����������� � ��������� ������� (0,0)
                canvas.DrawBitmap(loadedImage, 0, 0);
                // ��������������� ��������� �������
                canvas.Restore();

                // ��������� ���� ����������� �����
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = 4,
                    IsAntialias = true
                })
                {
                    foreach (var mark in allMarks)
                    {
                        if (mark.Count >= 2)
                        {
                            using (var path = new SKPath())
                            {
                                path.MoveTo(mark[0]);
                                for (int i = 1; i < mark.Count; i++)
                                {
                                    path.LineTo(mark[i]);
                                }
                                canvas.DrawPath(path, paint);
                            }
                        }
                        else if (mark.Count == 1)
                        {
                            canvas.DrawCircle(mark[0], 5, paint);
                        }
                    }

                    // ��������� ������� �����
                    if (currentMark.Count >= 2)
                    {
                        using (var path = new SKPath())
                        {
                            path.MoveTo(currentMark[0]);
                            for (int i = 1; i < currentMark.Count; i++)
                            {
                                path.LineTo(currentMark[i]);
                            }
                            canvas.DrawPath(path, paint);
                        }
                    }
                    else if (currentMark.Count == 1)
                    {
                        canvas.DrawCircle(currentMark[0], 5, paint);
                    }
                }
            }
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            if (loadedImage == null || !isMarkMode) return;

            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    // ������ ����� �����
                    currentMark = new List<SKPoint> { e.Location };
                    canvasView.InvalidateSurface();
                    break;

                case SKTouchAction.Moved:
                    // ���������� ����� � ������� �����
                    if (currentMark.Count > 0)
                    {
                        currentMark.Add(e.Location);
                        canvasView.InvalidateSurface();
                    }
                    break;

                case SKTouchAction.Released:
                    // ���������� �����
                    if (currentMark.Count > 0)
                    {
                        allMarks.Add(new List<SKPoint>(currentMark));
                        currentMark.Clear();
                        canvasView.InvalidateSurface();
                    }
                    break;
            }

            e.Handled = true;
        }

        private void OnMarkModeClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            markModeButton.Text = isMarkMode ? "��������" : "�����";
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (allMarks.Count > 0)
            {
                allMarks.RemoveAt(allMarks.Count - 1);
                canvasView.InvalidateSurface();
            }
        }

        private async void OnSavePhotoClicked(object sender, EventArgs e)
        {
            if (loadedImage == null)
            {
                await DisplayAlert("��������������", "��� ����������� ��� ����������", "OK");
                return;
            }

            try
            {
                // �������� ������ ����������� � �������
                var info = new SKImageInfo(loadedImage.Width, loadedImage.Height);
                using (var surface = SKSurface.Create(info))
                {
                    var canvas = surface.Canvas;
                    canvas.Clear(SKColors.White);
                    canvas.DrawBitmap(loadedImage, 0, 0);

                    // �������� �������������� ��������� ����� �� �������� � ���������� �����������
                    var invertedMatrix = new SKMatrix();
                    if (matrix.TryInvert(out invertedMatrix))
                    {
                        // ��������� ���� ����� �� �����������
                        using (var paint = new SKPaint
                        {
                            Style = SKPaintStyle.Stroke,
                            Color = SKColors.Red,
                            StrokeWidth = 2,
                            IsAntialias = true
                        })
                        {
                            foreach (var mark in allMarks)
                            {
                                if (mark.Count >= 2)
                                {
                                    using (var path = new SKPath())
                                    {
                                        // �������������� �������� ��������� � ���������� �����������
                                        var imagePoint = invertedMatrix.MapPoint(mark[0]);
                                        path.MoveTo(imagePoint);

                                        for (int i = 1; i < mark.Count; i++)
                                        {
                                            imagePoint = invertedMatrix.MapPoint(mark[i]);
                                            path.LineTo(imagePoint);
                                        }
                                        canvas.DrawPath(path, paint);
                                    }
                                }
                                else if (mark.Count == 1)
                                {
                                    var imagePoint = invertedMatrix.MapPoint(mark[0]);
                                    canvas.DrawCircle(imagePoint, 3, paint);
                                }
                            }
                        }
                    }

                    // ��������� ����������� �� �����������
                    using (var image = surface.Snapshot())
                    using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
                    {
                        // ���������� � ����
                        var filePath = Path.Combine(FileSystem.CacheDirectory, $"MarkedImage_{DateTime.Now.Ticks}.png");
                        using (var fs = File.OpenWrite(filePath))
                        {
                            data.SaveTo(fs);
                        }

                        // ����������� � �������
                        await Share.RequestAsync(new ShareFileRequest
                        {
                            Title = "��������� ���������� ����",
                            File = new ShareFile(filePath)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� �����������: {ex.Message}", "OK");
            }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            // ������������� ������� ��� ��������� ������� ������
            if (loadedImage != null && width > 0 && height > 0)
            {
                CalculateImageMatrix();
                canvasView.InvalidateSurface();
            }
        }
    }
}