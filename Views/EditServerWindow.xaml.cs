using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using SplateClientDesktop.Models;

namespace SplateClientDesktop.Views;

public partial class EditServerWindow
{
    public ServerNode Server { get; private set; }
    public bool IsSaved { get; private set; }
    public ServerNode? ResultNode => IsSaved ? Server : null;

    public EditServerWindow(ServerNode? server = null)
    {
        InitializeComponent();
        Server = server != null
            ? server.Clone()
            : new ServerNode { Name = "Новый сервер", Protocol = "vless", Port = 443, Security = "reality", Network = "tcp" };
        PopulateFields();
    }

    private void PopulateFields()
    {
        TxtName.Text = Server.Name;
        TxtAddress.Text = Server.Address;
        TxtPort.Text = Server.Port.ToString();
        TxtUuid.Text = Server.Uuid;
        TxtEncryption.Text = Server.Encryption;
        TxtAlpn.Text = Server.Alpn;
        TxtSni.Text = Server.Sni;
        TxtPublicKey.Text = Server.PublicKey;
        TxtShortId.Text = Server.ShortId;
        TxtSpiderX.Text = Server.SpiderX;
        TxtFlow.Text = Server.Flow;
        TxtPath.Text = Server.Path;
        TxtHost.Text = Server.Host;
        TxtPadding.Text = Server.Padding;

        SelectComboBoxByTag(CmbProtocol, Server.Protocol);
        SelectComboBoxByTag(CmbNetwork, Server.Network);
        SelectComboBoxByTag(CmbSecurity, Server.Security);
        SelectComboBoxByTag(CmbFingerprint, Server.Fingerprint);
        SelectComboBoxByTag(CmbMode, Server.Mode);

        UpdateUiVisibility();
    }

    private void SelectComboBoxByTag(ComboBox cmb, string? tagValue)
    {
        if (string.IsNullOrEmpty(tagValue)) return;
        foreach (var item in cmb.Items)
        {
            if (item is ComboBoxItem cbi && string.Equals(cbi.Tag?.ToString(), tagValue, StringComparison.OrdinalIgnoreCase))
            {
                cmb.SelectedItem = cbi;
                return;
            }
        }
        if (cmb.Items.Count > 0 && cmb.SelectedItem == null)
        {
            cmb.SelectedIndex = 0;
        }
    }

    private string GetSelectedTag(ComboBox cmb, string defaultVal = "")
    {
        if (cmb.SelectedItem is ComboBoxItem cbi && cbi.Tag != null)
        {
            return cbi.Tag.ToString() ?? defaultVal;
        }
        return defaultVal;
    }

    private void CmbProtocol_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUiVisibility();
    }

    private void CmbNetwork_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUiVisibility();
    }

    private void CmbSecurity_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateUiVisibility();
    }

    private void UpdateUiVisibility()
    {
        if (LblUuid == null) return;

        var proto = GetSelectedTag(CmbProtocol, "vless");
        var sec = GetSelectedTag(CmbSecurity, "reality");
        var net = GetSelectedTag(CmbNetwork, "tcp");

        LblUuid.Text = (proto == "trojan" || proto == "shadowsocks") ? "Пароль (Password)" : "UUID пользователя";

        if (PnlReality != null)
        {
            PnlReality.Visibility = sec == "reality" ? Visibility.Visible : Visibility.Collapsed;
        }

        if (PnlXhttp != null)
        {
            PnlXhttp.Visibility = net == "xhttp" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var address = TxtAddress.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            MessageBox.Show("Укажите адрес сервера (домен или IP).", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtAddress.Focus();
            return;
        }

        if (!int.TryParse(TxtPort.Text.Trim(), out var port) || port <= 0 || port > 65535)
        {
            MessageBox.Show("Укажите корректный порт (1 - 65535).", "Ошибка валидации", MessageBoxButton.OK, MessageBoxImage.Warning);
            TxtPort.Focus();
            return;
        }

        Server.Name = TxtName.Text.Trim();
        Server.Address = address;
        Server.Port = port;
        Server.Protocol = GetSelectedTag(CmbProtocol, "vless");
        Server.Network = GetSelectedTag(CmbNetwork, "tcp");
        Server.Security = GetSelectedTag(CmbSecurity, "reality");
        Server.Uuid = TxtUuid.Text.Trim();
        Server.Encryption = TxtEncryption.Text.Trim();
        Server.Alpn = TxtAlpn.Text.Trim();
        if (string.IsNullOrWhiteSpace(Server.Uuid) || (Server.Protocol == "shadowsocks" && (string.IsNullOrWhiteSpace(Server.Encryption) || Server.Encryption == "none")))
        {
            MessageBox.Show("Укажите UUID/пароль и метод шифрования для Shadowsocks.", "Проверьте конфигурацию", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Server.Sni = TxtSni.Text.Trim();
        Server.Fingerprint = GetSelectedTag(CmbFingerprint, "chrome");
        Server.PublicKey = TxtPublicKey.Text.Trim();
        Server.ShortId = TxtShortId.Text.Trim();
        Server.SpiderX = TxtSpiderX.Text.Trim();
        Server.Flow = TxtFlow.Text.Trim();
        Server.Path = TxtPath.Text.Trim();
        Server.Host = TxtHost.Text.Trim();
        Server.Mode = GetSelectedTag(CmbMode, "auto");
        Server.Padding = TxtPadding.Text.Trim();

        // Regenerate RawLink if it was custom
        Server.RawLink = Services.LinkParser.GenerateLink(Server);

        IsSaved = true;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
