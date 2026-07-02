using System.Windows;

namespace QuickPanel.Settings;

/// <summary>
/// Diálogo de reconciliación al vincular una PC nueva cuando ambos lados (local y nube)
/// ya tienen datos. Reemplaza el MessageBox genérico de Fase 2. <see cref="UseCloud"/>
/// indica la elección: true = usar la nube (bajar), false = mantener esta PC (subir).
/// </summary>
public partial class SyncReconcileDialog : Window
{
    public bool UseCloud { get; private set; }

    public SyncReconcileDialog()
    {
        InitializeComponent();
    }

    private void UseCloud_Click(object sender, RoutedEventArgs e)
    {
        UseCloud = true;
        DialogResult = true;
    }

    private void KeepLocal_Click(object sender, RoutedEventArgs e)
    {
        UseCloud = false;
        DialogResult = true;
    }
}
