using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using DraftEngine.App.ViewModels.NewContract;
using Serilog;

namespace DraftEngine.App.ViewModels;

public partial class NewContractViewModel
{
    private bool _isActive;
    private bool _isRestoringDraft;
    private Task _draftSaveTask = Task.CompletedTask;
    private Task _draftRestoreTask = Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanDiscardDraft))]
    private async Task DiscardDraftAsync()
    {
        if (!_dialogs.Confirm("Discard the saved contract draft and clear the wizard?", "Discard draft"))
        {
            return;
        }

        if (!await DeleteSavedDraftAsync())
        {
            _dialogs.ShowError("The saved draft could not be cleared. Please try again.");
            return;
        }

        _isRestoringDraft = true;
        try
        {
            ResetWizard();
        }
        finally
        {
            _isRestoringDraft = false;
            _isActive = true;
        }
    }

    private bool CanDiscardDraft() => !IsBusy;

    private void ActivateWizard()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _draftRestoreTask = RestoreDraftAsync();
    }

    private async Task RestoreDraftAsync()
    {
        _isRestoringDraft = true;
        ResetWizard();
        try
        {
            var serializedDraft = await _drafts.LoadDraftAsync(NewContractDraftKey);
            if (string.IsNullOrWhiteSpace(serializedDraft))
            {
                return;
            }

            var draft = JsonSerializer.Deserialize<WizardDraftPayload>(serializedDraft);
            if (draft is null)
            {
                return;
            }

            var selectedType = ContractTypes.FirstOrDefault(
                type => string.Equals(type.Id, draft.ContractTypeId, StringComparison.OrdinalIgnoreCase));
            if (selectedType is not null)
            {
                foreach (var type in ContractTypes)
                {
                    type.IsSelected = ReferenceEquals(type, selectedType);
                }

                SelectedContractType = selectedType;
            }

            RestoreIntakeAnswers(draft.IntakeAnswers);

            if (draft.CurrentStep >= 3 || draft.SelectedClauseIds.Count > 0)
            {
                await LoadClausesAsync();
                var selectedIds = draft.SelectedClauseIds.ToHashSet();
                foreach (var clause in AllClauseOptions())
                {
                    clause.IsSelected = selectedIds.Contains(clause.Id);
                }
            }

            CurrentStep = Math.Clamp(draft.CurrentStep, 1, 4);
            if (CurrentStep == 4)
            {
                BuildSummary();
            }
        }
        catch
        {
            // Draft content and decryption/serialization details must never reach logs.
            Log.Warning("The saved new-contract draft could not be restored.");
        }
        finally
        {
            _isRestoringDraft = false;
        }
    }

    private void RestoreIntakeAnswers(Dictionary<string, string>? answers)
    {
        if (answers is null)
        {
            return;
        }

        foreach (var field in IntakeFields)
        {
            if (!answers.TryGetValue(field.Key, out var value))
            {
                continue;
            }

            switch (field.Kind)
            {
                case IntakeFieldKind.Date:
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
                    {
                        field.DateValue = date;
                    }
                    break;
                case IntakeFieldKind.Choice:
                    field.SelectedChoice = value;
                    break;
                default:
                    field.Value = value;
                    break;
            }
        }
    }

    private void QueueDraftSave(bool force = false)
    {
        if (_isRestoringDraft || (!_isActive && !force))
        {
            return;
        }

        var serializedDraft = JsonSerializer.Serialize(CaptureDraft());
        var previousSave = _draftSaveTask;
        _draftSaveTask = SaveDraftAfterAsync(previousSave, serializedDraft);
    }

    private async Task SaveDraftAfterAsync(Task previousSave, string serializedDraft)
    {
        await previousSave;
        try
        {
            await _drafts.SaveDraftAsync(NewContractDraftKey, serializedDraft);
        }
        catch
        {
            // Do not include exceptions or payloads in logs; draft answers may be sensitive.
            Log.Warning("The new-contract draft could not be saved.");
            DraftErrorMessage = "Your latest wizard changes could not be saved. Please keep DraftEngine open and try again.";
        }
    }

    private WizardDraftPayload CaptureDraft()
    {
        var answers = IntakeFields.ToDictionary(
            field => field.Key,
            field => field.Kind == IntakeFieldKind.Date
                ? field.DateValue?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
                : field.EffectiveValue,
            StringComparer.OrdinalIgnoreCase);

        return new WizardDraftPayload
        {
            ContractTypeId = SelectedContractType?.Id,
            IntakeAnswers = answers,
            SelectedClauseIds = AllClauseOptions()
                .Where(clause => clause.IsSelected)
                .Select(clause => clause.Id)
                .ToList(),
            CurrentStep = CurrentStep
        };
    }

    private async Task<bool> DeleteSavedDraftAsync()
    {
        _isActive = false;
        try
        {
            await _draftRestoreTask;
            await _draftSaveTask;
            await _drafts.DeleteDraftAsync(NewContractDraftKey);
            DraftErrorMessage = null;
            return true;
        }
        catch
        {
            Log.Warning("The new-contract draft could not be cleared.");
            return false;
        }
    }

    private async Task SaveDraftWhenLeavingAsync()
    {
        try
        {
            await _draftRestoreTask;
            QueueDraftSave(force: true);
            await _draftSaveTask;
        }
        catch
        {
            Log.Warning("The new-contract draft could not be saved.");
            DraftErrorMessage = "Your wizard draft could not be saved.";
        }
    }

    private sealed class WizardDraftPayload
    {
        public string? ContractTypeId { get; set; }
        public Dictionary<string, string> IntakeAnswers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<int> SelectedClauseIds { get; set; } = new();
        public int CurrentStep { get; set; } = 1;
    }
}
