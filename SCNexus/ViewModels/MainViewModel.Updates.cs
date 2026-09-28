using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    [ObservableProperty] private bool isUpdateBusy;
    [ObservableProperty] private string updateStatus = "Нажми кнопку, чтобы проверить новую версию.";
    public string CurrentAppVersion => $"Установлена версия {updateService.CurrentVersion.ToString(3)}";
    public string GithubTokenStatus => updateService.HasToken ? "Доступ к закрытому репозиторию сохранён." : "Для закрытого репозитория нужен токен GitHub с правом Contents: Read.";
    public string? PendingInstallerPath { get; private set; }

    partial void OnIsUpdateBusyChanged(bool value) => CheckForUpdatesCommand.NotifyCanExecuteChanged();
    private bool CanCheckForUpdates() => !IsUpdateBusy;

    public void SaveGithubToken(string token)
    {
        try
        {
            updateService.SaveToken(token);
            OnPropertyChanged(nameof(GithubTokenStatus));
            UpdateStatus = "Токен сохранён для текущего пользователя Windows.";
        }
        catch (Exception ex) { UpdateStatus = $"Не удалось сохранить токен: {ex.Message}"; }
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
            if (MessageBox.Show($"Доступна версия {release.Version.ToString(3)}. Скачать и установить обновление?",
                "SC NEXUS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                UpdateStatus = $"Доступна версия {release.Version.ToString(3)}. Установка отменена.";
                return;
            }
            var progress = new Progress<int>(percent => UpdateStatus = $"Скачиваю установщик: {percent}%");
            var installer = await updateService.DownloadInstallerAsync(release, progress);
            PendingInstallerPath = installer;
            UpdateStatus = "Обновление готово. Закрываю приложение: установка пройдёт в фоне, затем SC NEXUS откроется снова…";
            Application.Current.MainWindow.Close();
        }
        catch (Exception ex) { UpdateStatus = $"Не удалось обновить программу: {ex.Message}"; }
        finally { IsUpdateBusy = false; }
    }
}
