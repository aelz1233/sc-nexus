using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SCNexus.Models;
using SCNexus.Services;

namespace SCNexus.ViewModels;

public partial class MainViewModel
{
    private ComponentShoppingPlan? _shoppingPlan;
    private string _shoppingShip = "";
    private bool _showShoppingChecklist = true;
    private int _shoppingStopIndex;
    private bool _shoppingCompleted;
    public ObservableCollection<OverlayChecklistEntry> OverlayChecklist { get; } = [];
    public bool HasShoppingGuidance => _shoppingPlan is not null;
    public bool HasOverlayChecklist => HasShoppingGuidance || HasActiveVoyage;
    public bool CanSwitchOverlayChecklist => HasShoppingGuidance && HasActiveVoyage;
    private bool ShowingShopping => HasShoppingGuidance && (_showShoppingChecklist || !HasActiveVoyage);
    private ComponentShoppingStop? ShoppingStop => !_shoppingCompleted && _shoppingPlan is { Stops.Count: > 0 } plan
        ? plan.Stops[Math.Clamp(_shoppingStopIndex, 0, plan.Stops.Count - 1)] : null;
    public string OverlayChecklistTitle => ShowingShopping
        ? (IsEnglish ? $"COMPONENTS · {_shoppingShip}" : $"КОМПОНЕНТЫ · {_shoppingShip}")
        : (IsEnglish ? "TRIP CHECKLIST" : "ЧЕК-ЛИСТ РЕЙСА");
    public string OverlayChecklistStop => ShowingShopping
        ? ShoppingStop?.Heading ?? (IsEnglish ? "Purchases complete — install the components in Vehicle Loadout." : "Всё куплено — установи компоненты в Vehicle Loadout.")
        : ActiveVoyageStopDisplay;
    public string OverlayChecklistSummary => ShowingShopping
        ? (IsEnglish ? $"Purchased: {PurchasedCount}/{ShoppingCount} · remaining {ShoppingRemaining:N0} aUEC" : $"Куплено: {PurchasedCount}/{ShoppingCount} · осталось {ShoppingRemaining:N0} aUEC")
        : (IsEnglish ? $"Checked: {OverlayChecklist.Count(x => x.IsDone)}/{OverlayChecklist.Count}" : $"Отмечено: {OverlayChecklist.Count(x => x.IsDone)}/{OverlayChecklist.Count}");
    public string OverlayChecklistNext => ShowingShopping
        ? _shoppingPlan?.Stops.FirstOrDefault(x => x.Number > (ShoppingStop?.Number ?? int.MaxValue) && x.Items.Any(i => !i.IsPurchased)) is { } next
            ? (IsEnglish ? $"Next: {next.Heading}" : $"Далее: {next.Heading}") : ""
        : ActiveVoyageNextDisplay;
    public bool OverlayChecklistDanger => ShowingShopping ? ShoppingStop?.IsPyro == true : ActiveVoyageStop?.IsPyro == true;
    public string OverlayChecklistSwitchText => ShowingShopping ? (IsEnglish ? "Trip" : "Рейс") : (IsEnglish ? "Components" : "Компоненты");
    public string OverlayChecklistNextButton => IsEnglish ? "Stop completed" : "Остановка выполнена";
    public string OverlayChecklistCloseText => IsEnglish ? "Stop tracking purchases" : "Убрать закупку из оверлея";
    public string OverlayChecklistPreviousText => IsEnglish ? "Previous stop" : "Предыдущая остановка";
    public bool CanGoToPreviousChecklistStop => ShowingShopping ? _shoppingStopIndex > 0 || _shoppingCompleted : ActiveVoyageStopIndex > 0 || ActiveVoyageCompleted;
    public bool CanCompleteChecklistStop => (ShowingShopping ? ShoppingStop is not null : HasActiveVoyage && !ActiveVoyageCompleted) && OverlayChecklist.All(x => x.IsDone);
    private int PurchasedCount => _shoppingPlan?.Stops.Sum(x => x.Items.Where(i => i.IsPurchased).Sum(i => i.Quantity)) ?? 0;
    private int ShoppingCount => _shoppingPlan?.Stops.Sum(x => x.Items.Sum(i => i.Quantity)) ?? 0;
    private decimal ShoppingRemaining => _shoppingPlan?.Stops.Sum(x => x.Items.Where(i => !i.IsPurchased).Sum(i => i.Quantity * i.UnitPrice)) ?? 0;

    public void TrackComponentShopping(ComponentShoppingPlan plan, string ship)
    {
        SetShoppingPlan(plan, ship);
        _showShoppingChecklist = true;
        OverlayExpanded = true;
        OverlayPreview = true;
        RefreshOverlayChecklist();
        QueueSave();
    }

    private void SetShoppingPlan(ComponentShoppingPlan? plan, string ship)
    {
        if (_shoppingPlan is not null)
            foreach (var item in _shoppingPlan.Stops.SelectMany(x => x.Items)) item.PropertyChanged -= OnShoppingItemChanged;
        _shoppingPlan = plan;
        _shoppingShip = ship;
        _shoppingStopIndex = 0;
        _shoppingCompleted = false;
        if (plan is not null)
            foreach (var item in plan.Stops.SelectMany(x => x.Items)) item.PropertyChanged += OnShoppingItemChanged;
        RefreshOverlayChecklist();
    }

    private void OnShoppingItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ComponentShoppingItem.IsPurchased)) return;
        RefreshOverlayChecklist();
        QueueSave();
    }

    [RelayCommand]
    private void SwitchOverlayChecklist()
    {
        _showShoppingChecklist = !_showShoppingChecklist;
        RefreshOverlayChecklist();
    }

    [RelayCommand]
    private void CloseShoppingGuidance()
    {
        SetShoppingPlan(null, "");
        QueueSave();
    }

    [RelayCommand(CanExecute = nameof(CanCompleteChecklistStop))]
    private void CompleteChecklistStop()
    {
        if (!ShowingShopping) { NextVoyageStop(); return; }
        if (_shoppingStopIndex + 1 < _shoppingPlan!.Stops.Count) _shoppingStopIndex++;
        else _shoppingCompleted = true;
        RefreshOverlayChecklist();
        QueueSave();
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousChecklistStop))]
    private void PreviousChecklistStop()
    {
        if (!ShowingShopping)
        {
            if (ActiveVoyageCompleted) { ActiveVoyageCompleted = false; RefreshActiveVoyageProperties(); QueueSave(); }
            else PreviousVoyageStop();
            return;
        }
        if (_shoppingCompleted) _shoppingCompleted = false;
        else _shoppingStopIndex = Math.Max(0, _shoppingStopIndex - 1);
        RefreshOverlayChecklist();
        QueueSave();
    }

    private void RefreshOverlayChecklist()
    {
        OverlayChecklist.Clear();
        if (ShowingShopping && ShoppingStop is { } stop)
        {
            foreach (var item in stop.Items)
                OverlayChecklist.Add(new OverlayChecklistEntry(item.Display, item.IsPurchased, done => item.IsPurchased = done));
        }
        else if (!ShowingShopping && !ActiveVoyageCompleted && ActiveVoyageStop is { } voyageStop)
        {
            foreach (var raw in voyageStop.Action.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var action = ExpectedActions(voyageStop with { Action = raw }).FirstOrDefault();
                if (action is null) continue;
                OverlayChecklist.Add(new OverlayChecklistEntry(LocalizationService.T(raw), _completedVoyageActions.Contains(action.Key), done =>
                {
                    if (done) _completedVoyageActions.Add(action.Key); else _completedVoyageActions.Remove(action.Key);
                    OnPropertyChanged(nameof(OverlayChecklistSummary));
                    OnPropertyChanged(nameof(CanCompleteChecklistStop));
                    CompleteChecklistStopCommand.NotifyCanExecuteChanged();
                    QueueSave();
                }));
            }
        }
        foreach (var name in new[] { nameof(HasShoppingGuidance), nameof(HasOverlayChecklist), nameof(CanSwitchOverlayChecklist),
            nameof(OverlayChecklistTitle), nameof(OverlayChecklistStop), nameof(OverlayChecklistSummary), nameof(OverlayChecklistNext),
            nameof(OverlayChecklistDanger), nameof(OverlayChecklistSwitchText), nameof(CanCompleteChecklistStop), nameof(CanGoToPreviousChecklistStop) }) OnPropertyChanged(name);
        CompleteChecklistStopCommand.NotifyCanExecuteChanged();
        PreviousChecklistStopCommand.NotifyCanExecuteChanged();
    }

    private string SerializeShoppingGuidance() => _shoppingPlan is null ? "" : JsonSerializer.Serialize(new ShoppingGuidance(_shoppingPlan, _shoppingShip, _shoppingStopIndex, _shoppingCompleted));
    private void RestoreShoppingGuidance(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<ShoppingGuidance>(json);
            if (saved?.Plan?.Stops is { Count: > 0 } stops && stops.All(x => x.Items is not null))
            {
                SetShoppingPlan(saved.Plan, saved.Ship ?? "");
                _shoppingStopIndex = Math.Clamp(saved.StopIndex, 0, stops.Count - 1);
                _shoppingCompleted = saved.Completed;
                RefreshOverlayChecklist();
            }
        }
        catch (JsonException) { }
    }

    private sealed record ShoppingGuidance(ComponentShoppingPlan Plan, string Ship, int StopIndex = 0, bool Completed = false);

    public sealed partial class OverlayChecklistEntry(string text, bool done, Action<bool> changed) : ObservableObject
    {
        public string Text { get; } = text;
        [ObservableProperty] private bool isDone = done;
        partial void OnIsDoneChanged(bool value) => changed(value);
    }
}
