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
        // ��������� ����������� � ���������
        private SKBitmap photo;
        private SKMatrix transformMatrix = SKMatrix.CreateIdentity();
        private SKMatrix previousTransformMatrix = SKMatrix.CreateIdentity();

        // ��������� ��������������� � �����������
        private float scale = 1.0f;
        private float minScale = 0.5f;
        private float maxScale = 5.0f;
        private SKPoint panPosition = new SKPoint(0, 0);
        private SKPoint lastTouchPoint;
        private long lastTouchTime;

        // ��������� ������
        private bool isPanning = false;
        private bool isScaling = false;
        private float pinchStartScale = 1.0f;
        private SKPoint pinchCenter;

        // ������ ������
        private bool isMarkMode = false;
        private int currentMarkNumber = 1;

        // ������� ����� ������������ ���� (� ��������� �� ������ ����)
        private const float CircleRadiusPercent = 0.02f; // 2% �� ������ ����
        private const float TextSizePercent = 0.02f;     // 2% �� ������ ����
        private const float StrokeWidthPercent = 0.002f; // 0.2% �� ������ ����

        // ���������� ����������� ������� ��� ��������
        private const float MinExportCircleRadius = 20f;
        private const float MinExportTextSize = 20f;
        private const float MinExportStrokeWidth = 2f;

        // ��������� ��� �������� ����� � ������� � �������
        private class Mark
        {
            public SKPoint PhotoPosition { get; set; }  // ������� � ����������� ����������
            public int Number { get; set; }
        }

        // ������� �������� ��� ������
        private List<Mark> marks = new List<Mark>();

        public PhotoEditorPage()
        {
            InitializeComponent();

            // ������������� ��������� �������� ��� ��������
            zoomSlider.Value = scale;
            zoomValueLabel.Text = $"{scale * 100:0}%";
        }

        #region ��������� �������� � ���������� ����

        private async void OnPickPhotoClicked(object sender, EventArgs e)
        {
            try
            {
                noPhotoLabel.IsVisible = false;
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                var result = await FilePicker.PickAsync(new PickOptions
                {
                    FileTypes = FilePickerFileType.Images,
                    PickerTitle = "�������� ����"
                });

                if (result != null)
                {
                    using (var stream = await result.OpenReadAsync())
                    {
                        // �������� �����������
                        photo = SKBitmap.Decode(stream);

                        // ����� �������� ��� �������� ������ �����������
                        marks.Clear();
                        currentMarkNumber = 1;
                        isMarkMode = false;
                        toggleMarkModeButton.Text = "����� �����";

                        // ����� ������� �������������
                        ResetViewToFit();

                        // �����������
                        canvasView.InvalidateSurface();
                    }
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� ����: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
                noPhotoLabel.IsVisible = photo == null;
            }
        }

        private void ResetViewToFit()
        {
            if (photo == null) return;

            // ������ ����������� ������ ������� � ����
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width;
            float photoHeight = photo.Height;

            // ���������� �������� ��� ���������� ���� �� ����� � ��������� ��������
            float scaleX = canvasWidth * 0.95f / photoWidth;
            float scaleY = canvasHeight * 0.95f / photoHeight;
            scale = Math.Min(scaleX, scaleY);
            scale = Math.Max(minScale, Math.Min(maxScale, scale));

            // ��������� ������� ��� ������ �������
            zoomSlider.ValueChanged -= OnZoomSliderValueChanged;
            zoomSlider.Value = scale;
            zoomSlider.ValueChanged += OnZoomSliderValueChanged;

            // ��������� ����� � ������� ���������
            zoomValueLabel.Text = $"{scale * 100:0}%";

            // ���������� ������� ��� �������������
            panPosition.X = (canvasWidth - photoWidth * scale) / 2;
            panPosition.Y = (canvasHeight - photoHeight * scale) / 2;

            // ���������� ������� �������������
            UpdateTransformMatrix();
        }

        private void UpdateTransformMatrix()
        {
            // ��������� ���������� ������� ��� ��������
            previousTransformMatrix = transformMatrix;

            // ������� ����� ������� � ������ �������� � �������
            transformMatrix = SKMatrix.CreateScale(scale, scale);
            transformMatrix = transformMatrix.PostConcat(SKMatrix.CreateTranslation(panPosition.X, panPosition.Y));
        }

        private async void OnSavePhotoClicked(object sender, EventArgs e)
        {
            if (photo == null)
            {
                await DisplayAlert("��������", "��� ������������ ����", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // ��������� ������������ �����������
                var photoCopy = photo.Copy();

                // ������ ����� ��������������� �� ����� �����������
                using (var canvas = new SKCanvas(photoCopy))
                {
                    // ������ �����
                    DrawMarksToCanvas(canvas, true);
                }

                // ����������� ������� � ������ ��� ����������
                using (var image = SKImage.FromBitmap(photoCopy))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) // PNG ��� ������� ��������
                {
                    var filename = $"MarkedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                    var cacheFilePath = Path.Combine(FileSystem.CacheDirectory, filename);

                    using (var fs = File.OpenWrite(cacheFilePath))
                    {
                        data.SaveTo(fs);
                    }

                    // ��������� � �������
                    await SaveToGallery(cacheFilePath, filename);

                    await DisplayAlert("�����", "���� ��������� � �������", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ��������� ����: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        // ����� ��� ���������� ���� � �������
        private async Task SaveToGallery(string filePath, string filename)
        {
            if (!File.Exists(filePath))
                return;

#if ANDROID
                try
                {
                    // ��������� ���������� �� ������ ��� Android
                    var status = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                    if (status != PermissionStatus.Granted)
                    {
                        status = await Permissions.RequestAsync<Permissions.StorageWrite>();
                        if (status != PermissionStatus.Granted)
                            return;
                    }

                    // ����� ����������� ������ � �����
                    var mediaStatus = await Permissions.CheckStatusAsync<Permissions.Media>();
                    if (mediaStatus != PermissionStatus.Granted)
                    {
                        mediaStatus = await Permissions.RequestAsync<Permissions.Media>();
                        if (mediaStatus != PermissionStatus.Granted)
                            return;
                    }

                    // ����� ����������� ������ ��� API 29+
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    var values = new Android.Content.ContentValues();
                    values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.DisplayName, filename);
                    values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.MimeType, "image/jpeg");
                    
                    // ���������� ������������� ���� ������ ��� Android API 29+
                    if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
                    {
                        values.Put(Android.Provider.MediaStore.Images.Media.InterfaceConsts.RelativePath, "Pictures/MarkApp");
                    }

                    var resolver = Android.App.Application.Context.ContentResolver;
                    var contentUri = Android.Provider.MediaStore.Images.Media.ExternalContentUri;
                    
                    if (contentUri != null)
                    {
                        var uri = resolver?.Insert(contentUri, values);

                        if (uri != null)
                        {
                            using var outputStream = resolver?.OpenOutputStream(uri);
                            if (outputStream != null)
                            {
                                await stream.CopyToAsync(outputStream);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving to gallery: {ex.Message}");
                    
                    // ��� API < 29 ���������� ������ ������
                    if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.Q)
                    {
                        try
                        {
                            var mediaScanIntent = new Android.Content.Intent(Android.Content.Intent.ActionMediaScannerScanFile);
                            var file = new Java.IO.File(filePath);
                            var uri = Android.Net.Uri.FromFile(file);
                            mediaScanIntent.SetData(uri);
                            Android.App.Application.Context.SendBroadcast(mediaScanIntent);
                        }
                        catch (Exception scanEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error with media scanner: {scanEx.Message}");
                        }
                    }
                }
#endif

#if IOS
            try
            {
                // ��� iOS ���������� Photos API
                var status = await Permissions.CheckStatusAsync<Permissions.Photos>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Photos>();
                    if (status != PermissionStatus.Granted)
                        return;
                }

                // ����������� ����������� � Photos (����������� �����)
                await Photos.PHPhotoLibrary.RequestAuthorizationAsync();

                // �������� ����� ��������� ���������� ����������
                var library = Photos.PHPhotoLibrary.SharedPhotoLibrary;
                if (library == null)
                    return;

                // ������� ����������� �� �����
                var image = UIKit.UIImage.FromFile(filePath);
                if (image == null)
                    return;

                // ��������� ����������� � �����������
                var tcs = new TaskCompletionSource<bool>();

                library.PerformChanges(() =>
                {
                    Photos.PHAssetCreationRequest.CreationRequestForAsset().AddResource(Photos.PHAssetResourceType.Photo,
                        Foundation.NSUrl.FromFilename(filePath), null);
                },
                (success, error) =>
                {
                    if (error != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"iOS photo save error: {error.LocalizedDescription}");
                        tcs.SetResult(false);
                    }
                    else
                    {
                        tcs.SetResult(success);
                    }
                });

                await tcs.Task;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving to iOS gallery: {ex.Message}");
            }
#endif
        }

        private async void OnSharePhotoClicked(object sender, EventArgs e)
        {
            if (photo == null)
            {
                await DisplayAlert("��������", "��� ������������ ����", "OK");
                return;
            }

            try
            {
                loadingIndicator.IsVisible = true;
                loadingIndicator.IsRunning = true;

                // ��������� ������������ �����������
                var photoCopy = photo.Copy();

                // ������ ����� ��������������� �� ����� �����������
                using (var canvas = new SKCanvas(photoCopy))
                {
                    // ������ �����
                    DrawMarksToCanvas(canvas, true);
                }

                // ����������� ������� � ������ ��� ����������
                using (var image = SKImage.FromBitmap(photoCopy))
                using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) // PNG ��� ������� ��������
                {
                    var filePath = Path.Combine(FileSystem.CacheDirectory, $"SharedPhoto_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                    using (var fs = File.OpenWrite(filePath))
                    {
                        data.SaveTo(fs);
                    }

                    // ������ �����
                    await Share.RequestAsync(new ShareFileRequest
                    {
                        Title = "���������� ���� � �������",
                        File = new ShareFile(filePath)
                    });
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("������", $"�� ������� ���������� ����: {ex.Message}", "OK");
            }
            finally
            {
                loadingIndicator.IsVisible = false;
                loadingIndicator.IsRunning = false;
            }
        }

        #endregion

        #region ��������� ��������� � �������

        private void OnCanvasViewPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var canvasWidth = e.Info.Width;
            var canvasHeight = e.Info.Height;

            // ������� ������
            canvas.Clear(SKColors.LightGray);

            if (photo == null)
                return;

            // ���������� ������� �������������
            canvas.Save();
            canvas.SetMatrix(transformMatrix);

            // ��������� ����
            canvas.DrawBitmap(photo, 0, 0);

            // ������� � �������� ����������� ��� ��������� �����
            canvas.Restore();

            // ��������� ����� � ������������� ��������
            DrawMarksToCanvas(canvas, false);
        }

        private void DrawMarksToCanvas(SKCanvas canvas, bool forExport)
        {
            if (photo == null || marks.Count == 0)
                return;

            // ������������ ������� ����� ������������ ����
            float photoWidth = photo.Width;
            float circleRadius = photoWidth * CircleRadiusPercent;
            float textSize = photoWidth * TextSizePercent;
            float strokeWidth = photoWidth * StrokeWidthPercent;

            // ��� �������� ���������� ����������� ��������
            if (forExport)
            {
                circleRadius = Math.Max(circleRadius, MinExportCircleRadius);
                textSize = Math.Max(textSize, MinExportTextSize);
                strokeWidth = Math.Max(strokeWidth, MinExportStrokeWidth);
            }

            foreach (var mark in marks)
            {
                // �������� ������� �����
                SKPoint position;

                if (forExport)
                {
                    // ��� �������� ���������� ���������� ���������� ��������
                    position = mark.PhotoPosition;
                }
                else
                {
                    // ��� ����������� �� ������ ����������� ���������� ���� � ��������
                    position = transformMatrix.MapPoint(mark.PhotoPosition);
                }

                // ������ ������
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = new SKColor(255, 0, 0, 128), // �������������� �������
                    IsAntialias = true
                })
                {
                    if (forExport)
                    {
                        canvas.DrawCircle(position, circleRadius, paint);
                    }
                    else
                    {
                        // ��������� ������� ��� ����������� �� ������
                        canvas.DrawCircle(position, circleRadius * scale, paint);
                    }
                }

                // ������ ������� ������
                using (var paint = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = SKColors.Red,
                    StrokeWidth = forExport ? strokeWidth : strokeWidth * scale,
                    IsAntialias = true
                })
                {
                    if (forExport)
                    {
                        canvas.DrawCircle(position, circleRadius, paint);
                    }
                    else
                    {
                        canvas.DrawCircle(position, circleRadius * scale, paint);
                    }
                }

                // ������ ����� �����
                using (var paint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = forExport ? textSize : textSize * scale,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center
                })
                {
                    float verticalOffset = forExport ? textSize / 3 : (textSize * scale) / 3;
                    canvas.DrawText(mark.Number.ToString(),
                                    position.X,
                                    position.Y + verticalOffset,
                                    paint);
                }
            }
        }

        private SKPoint ConvertToPhotoCoordinates(SKPoint viewPoint)
        {
            // ����������� ���������� ������ � ���������� ��������� ����
            SKMatrix invertedMatrix = new SKMatrix();
            if (!transformMatrix.TryInvert(out invertedMatrix))
                return viewPoint;

            SKPoint photoPoint = invertedMatrix.MapPoint(viewPoint);
            return photoPoint;
        }

        // ���������, ��������� �� ����� � �������� ����������
        private bool IsPointInsidePhoto(SKPoint photoPoint)
        {
            if (photo == null) return false;

            return photoPoint.X >= 0 && photoPoint.X < photo.Width &&
                   photoPoint.Y >= 0 && photoPoint.Y < photo.Height;
        }

        private void OnCanvasViewTouch(object sender, SKTouchEventArgs e)
        {
            if (photo == null)
                return;

            var touchPoint = e.Location;

            // ��������� �������� � ����������� �� ������
            if (isMarkMode)
            {
                HandleMarkModeTouch(e);
            }
            else
            {
                HandleViewModeTouch(e);
            }

            e.Handled = true;
        }

        private void HandleMarkModeTouch(SKTouchEventArgs e)
        {
            if (e.ActionType == SKTouchAction.Released)
            {
                // ����������� ���������� ������� � ���������� ����
                SKPoint photoPoint = ConvertToPhotoCoordinates(e.Location);

                // ���������, ��������� �� ����� � �������� ����������
                if (IsPointInsidePhoto(photoPoint))
                {
                    // ���� ������� ������� ����� � �������
                    marks.Add(new Mark
                    {
                        PhotoPosition = photoPoint,  // ��������� ������� � ����������� ����
                        Number = currentMarkNumber++
                    });
                    canvasView.InvalidateSurface();
                }
            }
        }

        private void HandleViewModeTouch(SKTouchEventArgs e)
        {
            switch (e.ActionType)
            {
                case SKTouchAction.Pressed:
                    isPanning = true;
                    lastTouchPoint = e.Location;
                    break;

                case SKTouchAction.Moved:
                    if (e.InContact && isPanning)
                    {
                        // ��������� ������� ���������������
                        var deltaX = e.Location.X - lastTouchPoint.X;
                        var deltaY = e.Location.Y - lastTouchPoint.Y;

                        panPosition.X += deltaX;
                        panPosition.Y += deltaY;

                        // ������������ ���������������
                        LimitPanningPosition();

                        UpdateTransformMatrix();
                        canvasView.InvalidateSurface();

                        lastTouchPoint = e.Location;
                    }
                    break;

                case SKTouchAction.Released:
                    isPanning = false;
                    break;
            }
        }

        // ����������� ���������������, ����� ����������� �� �������� �� ������� ������
        private void LimitPanningPosition()
        {
            if (photo == null) return;

            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            float photoWidth = photo.Width * scale;
            float photoHeight = photo.Height * scale;

            // ����� ������� ����������� - ���� ������ ������ ���� �� ������
            // ����������� �������� X - �������������: (photoWidth - ����������� ������� �����)
            // ������������ �������� X - �������������: (canvasWidth - ����������� ������� �����)
            float minVisiblePart = Math.Min(photoWidth, canvasWidth) * 0.25f; // ���� �� 25% ���� ������ ���� �����

            // ����������� �� �����������
            if (panPosition.X > canvasWidth - minVisiblePart)
                panPosition.X = canvasWidth - minVisiblePart;
            else if (panPosition.X + photoWidth < minVisiblePart)
                panPosition.X = minVisiblePart - photoWidth;

            // ����������� �� ��������� � ����� �� �������
            float minVisiblePartY = Math.Min(photoHeight, canvasHeight) * 0.25f;
            if (panPosition.Y > canvasHeight - minVisiblePartY)
                panPosition.Y = canvasHeight - minVisiblePartY;
            else if (panPosition.Y + photoHeight < minVisiblePartY)
                panPosition.Y = minVisiblePartY - photoHeight;
        }

        private void OnZoomSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (photo == null) return;

            // ��������� ������� ����� � ������ ������
            float canvasWidth = (float)canvasView.Width;
            float canvasHeight = (float)canvasView.Height;
            SKPoint centerScreen = new SKPoint(canvasWidth / 2, canvasHeight / 2);
            SKPoint beforeZoom = ConvertToPhotoCoordinates(centerScreen);

            // ������������� ����� ������� �� ��������
            scale = (float)e.NewValue;

            // ��������� ����� � ������� ��������� � ���������
            zoomValueLabel.Text = $"{scale * 100:0}%";

            // ��������� ������� �������������
            UpdateTransformMatrix();

            // ������������ �������, ����� ����� ������ ��������� � ��� �� ����� ����
            SKPoint afterZoom = ConvertToPhotoCoordinates(centerScreen);
            panPosition.X += (afterZoom.X - beforeZoom.X) * scale;
            panPosition.Y += (afterZoom.Y - beforeZoom.Y) * scale;

            // ����������� ���������������
            LimitPanningPosition();

            // ��� ��� ��������� ������� ����� ��������� �������
            UpdateTransformMatrix();

            // �������������� ������
            canvasView.InvalidateSurface();
        }

        #endregion

        #region ���������� �������� � ������ ��������

        private void OnToggleMarkModeClicked(object sender, EventArgs e)
        {
            isMarkMode = !isMarkMode;
            toggleMarkModeButton.Text = isMarkMode ? "����� ���������" : "����� �����";
        }

        private void OnUndoClicked(object sender, EventArgs e)
        {
            if (marks.Count > 0)
            {
                // ������� ��������� ����� � ��������� �������
                marks.RemoveAt(marks.Count - 1);
                if (currentMarkNumber > 1)
                    currentMarkNumber--;

                canvasView.InvalidateSurface();
            }
        }

        #endregion

        #region ��������� ��������� ������� ��������

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);

            // ��� ��������� ������� ���� ������������� �������
            if (photo != null && width > 0 && height > 0)
            {
                ResetViewToFit();
                canvasView.InvalidateSurface();
            }
        }

        #endregion
    }
}