using System.Windows.Forms;
using AudioSwitch.Services;
using Xunit;

namespace AudioSwitch.Tests;

public class HotkeyFormatTests
{
    [Fact]
    public void FormatHotkey_DefaultCtrlAltF9()
    {
        var config = new HotkeyConfig
        {
            Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Key = Keys.F9
        };
        Assert.Equal("Ctrl + Alt + F9", SettingsPanelManager.FormatHotkey(config));
    }

    [Fact]
    public void FormatHotkey_WinShiftCombo()
    {
        var config = new HotkeyConfig
        {
            Modifiers = HotkeyModifiers.Win | HotkeyModifiers.Shift,
            Key = Keys.M
        };
        Assert.Equal("Shift + Win + M", SettingsPanelManager.FormatHotkey(config));
    }

    [Fact]
    public void FormatHotkey_EmptyWhenNoKey()
    {
        var config = new HotkeyConfig
        {
            Modifiers = HotkeyModifiers.None,
            Key = Keys.None
        };
        Assert.Equal("", SettingsPanelManager.FormatHotkey(config));
    }
}
