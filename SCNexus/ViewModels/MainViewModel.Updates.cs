using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool isUpdateBusy;
    [ObservableProperty] private string updateStatus = "Нажми кнопку, чтобы проверить новую версию.";
    public string CurrentAppVersion => $"Установлена версия {updateService.CurrentVersion.ToString(3)}";
    public string GithubTokenStatus => updateService.HasToken
        ? "Токен сохранён для доступа к закрытому репозиторию."
        : "Токен не сохранён. Для открытого репозитория он не нужен.";
    public string? PendingInstallerPath { get; private set; }

    partial void OnIsUpdateBusyChanged(bool value) => CheckForUpdatesCommand.NotifyCanExecuteChanged();
    private bool CanCheckForUpdates() => !IsUpdateBusy;

    public bool SaveGithubToken(string token)
    {
        try
        {
            updateService.SaveToken(token);
            OnPropertyChanged(nameof(GithubTokenStatus));
            UpdateStatus = "Токен сохранён для текущего пользователя Windows.";
            return true;
        }
        catch (Exception ex) { UpdateStatus = $"Не удалось сохранить токен: {ex.Message}"; return false; }
    }

    [RelayCommand]
    private void ClearGithubToken()
    {
        try
        {
            updateService.ClearToken();
            OnPropertyChanged(nameof(GithubTokenStatus));
            UpdateStatus = "Токен удалён.";
        }
        catch (Exception ex) { UpdateStatus = $"Не удалось удалить токен: {ex.Message}"; }
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsUpdateBusy = true;
        UpdateStatus = "Проверяю GitHub…";
        try
        {
            var release = await updateService.GetLatestAsync();
            if (release.Version <= updateService.CurrentVersion)
            {
                UpdateStatus = $"Уже установлена актуальная версия {updateService.CurrentVersion.ToString(3)}.";
                return;
            }
            RaiseNotification($"update:{release.Version}",
                IsEnglish ? "Update available" : "Доступно обновление",
                IsEnglish ? $"SC NEXUS {release.Version.ToString(3)} is ready to download."
                    : $"SC NEXUS {release.Version.ToString(3)} готов к загрузке.",
                Models.NexusNotificationKind.Info, TimeSpan.FromMinutes(30));
            if (MessageBox.Show($"Доступна версия {release.Version.ToString(3)}. Скачать и установить обновление?",
                "SC NEXUS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                UpdateStatus = $"Доступна версия {release.Version.ToString(3)}. Установка отменена.";
                return;
            }
            var progress = new Progress<int>(percent => UpdateStatus = $"Скачиваю установщик: {percent}%");
            var installer = await updateService.DownloadInstallerAsync(release, progress);
            UpdateStatus = "Создаю резервную копию перед обновлением…";
            var databaseBackup = await settingsService.CreatePreUpdateBackupAsync(release.Version);
            updateService.PrepareRollback(release.Version, settingsService.DatabasePath, databaseBackup);
            PendingInstallerPath = installer;
            UpdateStatus = "Обновление готово. Закрываю приложение: установка пройдёт в фоне, затем SC NEXUS откроется снова…";
            System.Windows.Application.Current.MainWindow.Close();
        }
        catch (Exception ex) { UpdateStatus = $"Не удалось обновить программу: {ex.Message}"; }
        finally { IsUpdateBusy = false; }
    }
}
