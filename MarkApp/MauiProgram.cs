using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Syncfusion.Maui.Core.Hosting;
using System.Diagnostics;
using System.Reflection;

namespace MarkApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        try
        {
            // Диагностика загрузки сборок
            LogAssemblyInfo();

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureSyncfusionCore()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Добавляем логирование для диагностики
            builder.Logging.AddDebug();

            return builder.Build();
        }
        catch (Exception ex)
        {
            // Подробное логирование ошибки
            LogDetailedError(ex);
            throw;
        }
    }

    private static void LogAssemblyInfo()
    {
        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                Debug.WriteLine($"Loaded Assembly: {assembly.FullName}");
                Debug.WriteLine($"Location: {assembly.Location}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка при логировании сборок: {ex}");
        }
    }

    private static void LogDetailedError(Exception ex)
    {
        Debug.WriteLine($"ФАТАЛЬНАЯ ОШИБКА: {ex.Message}");
        Debug.WriteLine($"Стек-трейс: {ex.StackTrace}");

        // Если это агрегированное исключение, выводим все внутренние
        if (ex is AggregateException aggregateEx)
        {
            foreach (var innerEx in aggregateEx.InnerExceptions)
            {
                Debug.WriteLine($"Внутренняя ошибка: {innerEx.Message}");
                Debug.WriteLine($"Стек-трейс внутренней ошибки: {innerEx.StackTrace}");
            }
        }
        else if (ex.InnerException != null)
        {
            Debug.WriteLine($"Внутренняя ошибка: {ex.InnerException.Message}");
            Debug.WriteLine($"Стек-трейс внутренней ошибки: {ex.InnerException.StackTrace}");
        }
    }
}