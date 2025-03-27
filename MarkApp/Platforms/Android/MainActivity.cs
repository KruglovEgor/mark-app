using Android.App;
using Android.Content.PM;
using Android.OS;
using System.Diagnostics;

namespace MarkApp;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
          ConfigurationChanges = ConfigChanges.ScreenSize |
                                 ConfigChanges.Orientation |
                                 ConfigChanges.UiMode |
                                 ConfigChanges.ScreenLayout |
                                 ConfigChanges.SmallestScreenSize |
                                 ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            // Логирование диагностической информации
            LogDeviceInfo();

            // Запрос разрешений
            RequestPermissions();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка в OnCreate: {ex}");
            throw;
        }
    }

    private void LogDeviceInfo()
    {
        System.Diagnostics.Debug.WriteLine($"Android Version: {Android.OS.Build.VERSION.Release}");
        System.Diagnostics.Debug.WriteLine($"Device Model: {Android.OS.Build.Model}");
        System.Diagnostics.Debug.WriteLine($"Device Manufacturer: {Android.OS.Build.Manufacturer}");
    }

    private void RequestPermissions()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            RequestPermissions(new[]
            {
                global::Android.Manifest.Permission.ReadMediaImages
            }, 1);
        }
        else
        {
            RequestPermissions(new[]
            {
                global::Android.Manifest.Permission.ReadExternalStorage
            }, 1);
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        for (int i = 0; i < permissions.Length; i++)
        {
            System.Diagnostics.Debug.WriteLine($"Permission: {permissions[i]} - Status: {grantResults[i]}");
        }
    }
}