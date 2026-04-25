using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using RagnarokAutomation.Core;

namespace RagnarokReconnectWinUI.Pages;

public sealed partial class CharactersPage : Page
{
    public sealed class CharacterRow : INotifyPropertyChanged
    {
        public CharacterConfig Model { get; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public CharacterRow(CharacterConfig model)
        {
            Model = model;
            ResetEditFields();
        }

        public string CharacterName => Model.CharacterName;
        public string Username => Model.Username;
        public int SlotRow => Model.SlotRow;
        public int SlotColumn => Model.SlotColumn;

        public bool IsEditing { get; private set; }
        public Visibility DisplayVisibility => IsEditing ? Visibility.Collapsed : Visibility.Visible;
        public Visibility EditVisibility => IsEditing ? Visibility.Visible : Visibility.Collapsed;

        public string EditCharacterName { get; set; } = string.Empty;
        public string EditUsername { get; set; } = string.Empty;
        public string EditPassword { get; set; } = string.Empty;
        public double EditSlotRow { get; set; }
        public double EditSlotColumn { get; set; }

        public void StartEdit()
        {
            IsEditing = true;
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(DisplayVisibility));
            OnPropertyChanged(nameof(EditVisibility));
        }

        public void DiscardEdit()
        {
            IsEditing = false;
            ResetEditFields();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(DisplayVisibility));
            OnPropertyChanged(nameof(EditVisibility));
        }

        public void ApplyEdit()
        {
            Model.CharacterName = EditCharacterName.Trim();
            Model.Username = EditUsername.Trim();
            Model.PasswordCipherText = CredentialProtector.Protect(EditPassword.Trim());
            Model.SlotRow = (int)EditSlotRow;
            Model.SlotColumn = (int)EditSlotColumn;
            IsEditing = false;
            ResetEditFields();
            OnPropertyChanged(nameof(CharacterName));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(SlotRow));
            OnPropertyChanged(nameof(SlotColumn));
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(DisplayVisibility));
            OnPropertyChanged(nameof(EditVisibility));
        }

        private void ResetEditFields()
        {
            EditCharacterName = Model.CharacterName;
            EditUsername = Model.Username;
            EditPassword = CredentialProtector.Unprotect(Model.PasswordCipherText);
            EditSlotRow = Model.SlotRow;
            EditSlotColumn = Model.SlotColumn;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public ObservableCollection<CharacterRow> Characters { get; } = [];

    public CharactersPage()
    {
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        Characters.Clear();
        foreach (CharacterConfig character in App.Services.Configuration.Characters.OrderBy(c => c.CharacterName))
        {
            Characters.Add(new CharacterRow(character));
        }
    }

    private void SaveCharacter_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        string characterName = CharacterNameInput.Text.Trim();
        string username = UsernameInput.Text.Trim();
        string passwordCipher = CredentialProtector.Protect(PasswordInput.Password.Trim());
        if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        CharacterConfig? existing = App.Services.Configuration.Characters.FirstOrDefault(c =>
            string.Equals(c.CharacterName, characterName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            App.Services.Configuration.Characters.Add(new CharacterConfig
            {
                CharacterName = characterName,
                Username = username,
                PasswordCipherText = passwordCipher,
                SlotRow = (int)SlotRowInput.Value,
                SlotColumn = (int)SlotColumnInput.Value
            });
        }
        else
        {
            existing.Username = username;
            if (!string.IsNullOrEmpty(passwordCipher))
            {
                existing.PasswordCipherText = passwordCipher;
            }
            existing.SlotRow = (int)SlotRowInput.Value;
            existing.SlotColumn = (int)SlotColumnInput.Value;
        }

        Reload();
    }

    private async void SaveAll_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await App.Services.SaveConfigurationAsync();
    }

    private void EditCharacter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CharacterRow row)
        {
            return;
        }

        foreach (CharacterRow item in Characters.Where(c => c != row && c.IsEditing))
        {
            item.DiscardEdit();
        }

        row.StartEdit();
    }

    private async void SaveRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CharacterRow row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.EditCharacterName) || string.IsNullOrWhiteSpace(row.EditUsername))
        {
            return;
        }

        row.ApplyEdit();
        Reload();
        await App.Services.SaveConfigurationAsync();
    }

    private void DiscardRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CharacterRow row)
        {
            return;
        }

        row.DiscardEdit();
    }
}
