using BagAssistant.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.NativeWrapper;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace BagAssistant.Windows;

/// <summary>
/// Floating button strip that appears next to the player's inventory addon while it is open.
/// Provides a one-click "Smart Sort" and a configurable single-rule trigger.
/// </summary>
public sealed class InventoryOverlayWindow : Window, IDisposable
{
    private static readonly string[] InventoryAddons =
    [
        "Inventory",
        "InventoryLarge",
        "InventoryExpansion",
    ];

    private readonly BagAssistantPlugin plugin;
    private readonly IGameGui gameGui;
    private Configuration Config => plugin.Configuration;

    private Vector2 anchorPos;
    private Vector2 anchorSize;

    public InventoryOverlayWindow(BagAssistantPlugin plugin, IGameGui gameGui)
        : base("##BagAssistantInventoryOverlay",
               ImGuiWindowFlags.NoTitleBar
               | ImGuiWindowFlags.NoResize
               | ImGuiWindowFlags.NoMove
               | ImGuiWindowFlags.NoScrollbar
               | ImGuiWindowFlags.NoScrollWithMouse
               | ImGuiWindowFlags.NoCollapse
               | ImGuiWindowFlags.NoSavedSettings
               | ImGuiWindowFlags.NoFocusOnAppearing
               | ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        this.gameGui = gameGui;
        IsOpen = false;
        DisableWindowSounds = true;
        RespectCloseHotkey = false;
    }

    public void Dispose() { }

    public override bool DrawConditions()
    {
        if (!Config.ShowInventoryOverlay) return false;
        if (!TryGetInventoryRect(out anchorPos, out anchorSize)) return false;
        return true;
    }

    public override void PreDraw()
    {
        // Pin position above the inventory addon. Width is auto-resize.
        var pos = new Vector2(anchorPos.X, MathF.Max(0, anchorPos.Y - 36f));
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        // Slight transparency so it doesn't fight visually with the addon.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
    }

    public override void Draw()
    {
        if (plugin.IsSortQueueBusy)
        {
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), $"Sorting... {plugin.SortQueueRemaining}/{plugin.SortQueueTotal}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Stop##ovstop"))
                plugin.StopSort();
            ImGui.SameLine();
            if (plugin.CanUndo && ImGui.SmallButton("Undo##ovundo"))
                plugin.UndoLastSort();
            return;
        }

        // Row 1: Smart Sort, Rule, Undo
        if (ImGui.Button("Smart Sort##ov_smart", new Vector2(80, 0)))
        {
            plugin.RunSmartSort();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Sort everything: Gear -> Bag1, Food/Medicine -> Bag2,\nMaterials -> Bag3, Crystals/Materia -> Bag4.");
        }

        ImGui.SameLine();
        var rule = ResolveOverlayRule();
        if (rule != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, rule.GetColor() * new Vector4(1, 1, 1, 0.5f));
            if (ImGui.Button($"{rule.Name}##ov_rule", new Vector2(80, 0)))
            {
                plugin.RunSingleRule(rule);
            }
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"Run only this rule:\n{rule.Name}");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("(no rule)##ov_rule_none", new Vector2(80, 0));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Pick a rule for the overlay button\nin Settings tab.");
        }

        ImGui.SameLine();
        if (plugin.CanUndo)
        {
            if (ImGui.SmallButton("Undo##ov_undo"))
                plugin.UndoLastSort();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Reverse the last sort.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.SmallButton("Undo##ov_undo_dis");
            ImGui.EndDisabled();
        }

        // Row 2: Delete Junk, Presets
        if (ImGui.Button("Delete Junk##ov_junk", new Vector2(80, 0)))
        {
            plugin.DeleteJunk();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Delete all vendor trash (white rarity items).");

        ImGui.SameLine();
        if (ImGui.SmallButton("Gatherer"))
        {
            plugin.RunGathererSort();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Crystals & Materia top priority,\nmaterials next, rest in Bag2.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Raider"))
        {
            plugin.RunRaiderSort();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("High ilvl gear + consumables\nfor combat readiness.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Hoarder"))
        {
            plugin.RunHoarderSort();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Group by rarity (white/green/blue/purple)\nto find junk easily.");

        // Row 3: Advanced operations
        ImGui.Spacing();
        if (ImGui.SmallButton("Extract Materia##ov_materia"))
        {
            plugin.ExtractMateria();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Move all gear at 100% spiritbond to\nBag 1 for materia extraction.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Merge Stacks##ov_merge"))
        {
            plugin.MergeStacks();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Consolidate duplicate items\ninto single stacks.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Vendor Trash##ov_trash"))
        {
            plugin.GroupVendorTrash();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Group all vendor trash (grey items)\ninto Bag 4 for easy discard.");

        ImGui.SameLine();
        if (ImGui.SmallButton("BA##ov_open"))
            plugin.ToggleMainUi();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open Bag Assistant");
    }

    private SortRule? ResolveOverlayRule()
    {
        if (Config.Rules.Count == 0) return null;
        var idx = Config.OverlayRuleIndex;
        if (idx < 0 || idx >= Config.Rules.Count) return null;
        var rule = Config.Rules[idx];
        return rule.Enabled ? rule : null;
    }

    private bool TryGetInventoryRect(out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;

        foreach (var name in InventoryAddons)
        {
            AtkUnitBasePtr addon = gameGui.GetAddonByName(name, 1);
            if (addon.IsNull) continue;
            if (!addon.IsVisible) continue;

            var w = MathF.Max(40f, addon.ScaledWidth);
            var h = MathF.Max(40f, addon.ScaledHeight);
            position = addon.Position;
            size = new Vector2(w, h);
            return true;
        }
        return false;
    }
}
