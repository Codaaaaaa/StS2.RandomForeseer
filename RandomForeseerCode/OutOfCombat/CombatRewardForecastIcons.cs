using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using RandomForeseer.RandomForeseerCode.OutOfCombat.Nodes;
using STS2RitsuLib.Settings;

namespace RandomForeseer.RandomForeseerCode.OutOfCombat;

/// <summary>
/// Owns the combat reward forecast icon inside the vanilla top bar's room icon row.
/// </summary>
/// <remarks>
/// The icon is inserted to the left of the floor icon so it never competes with
/// <see cref="NextActPrediction"/>, which owns the slots between the floor icon and the boss icon and rewires
/// their focus neighbors. Only the floor icon's left neighbor is changed here.
/// </remarks>
internal static class CombatRewardForecastIcons
{
    private static readonly ConditionalWeakTable<NTopBar, ForecastIcon> IconsByTopBar = new();
    private static readonly List<ForecastIcon> ActiveIcons = [];
    private static bool _isSubscribed;

    public static void Initialize(NTopBar topBar)
    {
        IconsByTopBar.GetValue(topBar, static key => new ForecastIcon(key));

        if (!_isSubscribed)
        {
            _isSubscribed = true;

            // The icon must disappear as soon as the feature is switched off, not at the next top bar.
            ModSettingsBindingWriteEvents.ValueWritten += _ => Refresh();
        }
    }

    /// <summary>
    /// Applies the current enabled state to every live icon's visibility and input handling.
    /// </summary>
    public static void Refresh()
    {
        foreach (var icon in ActiveIcons.ToList())
        {
            icon.Refresh();
        }
    }

    private sealed class ForecastIcon
    {
        private readonly NTopBar _topBar;
        private readonly NCombatRewardForecastIcon _icon;

        public ForecastIcon(NTopBar topBar)
        {
            _topBar = topBar;

            _icon = NCombatRewardForecastIcon.Create();
            _icon.Name = $"{Entry.ModId}_CombatRewardForecast_Icon";

            var roomIcons = topBar.FloorIcon.GetParent();
            roomIcons.AddChildSafely(_icon);
            roomIcons.MoveChildSafely(_icon, topBar.FloorIcon.GetIndex());

            _icon.FocusNeighborTop = _icon.GetPath();
            _icon.FocusNeighborLeft = _icon.GetPath();
            _icon.FocusNeighborRight = topBar.FloorIcon.GetPath();

            _icon.Connect(Node.SignalName.TreeExiting, Callable.From(Unsubscribe));
            ActiveIcons.Add(this);

            Refresh();
        }

        public void Refresh()
        {
            var isEnabled = CombatRewardForecast.IsEnabled();

            _icon.Visible = isEnabled;
            _icon.FocusMode = isEnabled ? Control.FocusModeEnum.All : Control.FocusModeEnum.None;
            _icon.MouseFilter = isEnabled ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;

            _topBar.FloorIcon.FocusNeighborLeft = isEnabled
                ? _icon.GetPath()
                : _topBar.FloorIcon.GetPath();
        }

        private void Unsubscribe()
        {
            ActiveIcons.Remove(this);
        }
    }
}

[HarmonyPatch(typeof(NTopBar))]
internal static class CombatRewardForecastTopBarPatches
{
    [HarmonyPatch(nameof(NTopBar.Initialize))]
    [HarmonyPostfix]
    private static void Initialize(NTopBar __instance)
    {
        try
        {
            CombatRewardForecastIcons.Initialize(__instance);
        }
        catch (Exception ex)
        {
            // The icon scene lives in the mod PCK. A stale or missing PCK must not take the vanilla top bar
            // down with it, so failing to add the forecast icon degrades to not having one.
            Entry.Logger.Warn($"Combat reward forecast failed to initialize: {ex}");
        }
    }
}
