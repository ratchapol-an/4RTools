using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Controls;
using RagnarokAutomation.Core;

namespace RagnarokReconnectWinUI.Pages;

public sealed partial class ProcessesPage : Page
{
    public ObservableCollection<ProcessSnapshot> Processes => App.Services.KnownProcesses;

    public ProcessesPage()
    {
        InitializeComponent();
    }

    private void RefreshProcesses_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        App.Services.RefreshProcesses();
    }
}
