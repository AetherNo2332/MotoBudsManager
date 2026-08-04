using CommunityToolkit.Mvvm.ComponentModel;
using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;
using MotoBudsManager.Services;

namespace MotoBudsManager.ViewModels;

public sealed partial class DeviceCardViewModel : ObservableObject
{
    private readonly ProductPresentation _product;

    public DeviceCardViewModel(MotoBudsDevice device, int index)
    {
        Device = device;
        AutomationId = $"HomeDeviceCard-{index}";
        LeftBatteryAutomationId = $"HomeBatteryLeft-{index}";
        RightBatteryAutomationId = $"HomeBatteryRight-{index}";
        CaseBatteryAutomationId = $"HomeBatteryCase-{index}";
        LeftChargingAutomationId = $"HomeChargingLeft-{index}";
        RightChargingAutomationId = $"HomeChargingRight-{index}";
        CaseChargingAutomationId = $"HomeChargingCase-{index}";
        _product = ProductCatalog.Get(device.ModelId, device.ColorId);
        BatteryStatusText = device.BatteryPercent is { } battery
            ? string.Format(LocalizationService.Get("StandardBatteryFormat"), battery)
            : LocalizationService.Get("StandardBatteryUnavailable");
    }

    public MotoBudsDevice Device { get; }

    public string AutomationId { get; }

    public string LeftBatteryAutomationId { get; }

    public string RightBatteryAutomationId { get; }

    public string CaseBatteryAutomationId { get; }

    public string LeftChargingAutomationId { get; }

    public string RightChargingAutomationId { get; }

    public string CaseChargingAutomationId { get; }

    public string ChargingLabel => LocalizationService.Get("ChargingLabel");

    public string ProductName => _product.ProductName;

    public string ImageSource => $"ms-appx:///Assets/Products/{_product.ImageFileName}";

    public string ModelAndColor => $"{Device.ModelId}  ·  {_product.ColorName}";

    public string PairedName => Device.Name;

    [ObservableProperty]
    public partial string BatteryStatusText { get; set; }

    [ObservableProperty]
    public partial string LeftBatteryText { get; set; } = "—";

    [ObservableProperty]
    public partial string RightBatteryText { get; set; } = "—";

    [ObservableProperty]
    public partial string CaseBatteryText { get; set; } = "—";

    [ObservableProperty]
    public partial bool LeftIsCharging { get; set; }

    [ObservableProperty]
    public partial bool RightIsCharging { get; set; }

    [ObservableProperty]
    public partial bool CaseIsCharging { get; set; }

    public void UpdateBattery(EarbudBatteryState state)
    {
        BatteryStatusText = LocalizationService.Get("CurrentBatteryStatus");
        LeftBatteryText = FormatBattery(state.Left);
        RightBatteryText = FormatBattery(state.Right);
        CaseBatteryText = FormatBattery(state.Case);
        LeftIsCharging = state.Left.IsCharging;
        RightIsCharging = state.Right.IsCharging;
        CaseIsCharging = state.Case.IsCharging;
    }

    private static string FormatBattery(BatteryComponentState state)
    {
        if (state.Percent is null)
        {
            return "—";
        }

        return $"{state.Percent}%";
    }
}
