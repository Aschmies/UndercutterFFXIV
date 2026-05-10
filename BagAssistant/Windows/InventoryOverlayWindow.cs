using BagAssistant.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;

namespace BagAssistant.Windows;

/// <summary>
/// Floating button strip + zone overlay anchored to the player's inventory addon.
/// Slot positions are read directly from the live <c>InventoryGrid</c> addons so the overlay
/// is always pixel-aligned regardless of UI scale, addon (Inventory / InventoryLarge /
/// InventoryExpansion), or which tab is active. Bags 1-4 are all supported.
/// </summary>
public sealed unsafe class InventoryOverlayWindow : Window, IDisposable
{
    private readonly BagAssistantPlugin plugin;
    private readonly IGameGui gameGui;
    private Configuration Config => plugin.Configuration;

    private Vector2 anchorPos;
    private Vector2 anchorSize;
    private bool openJunkConfirmPopup = true;

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
        if (!TryGetInventoryAnchor(out anchorPos, out anchorSize)) return false;
        return true;
    }

    public override void PreDraw()
    {
        // Anchor the button strip so its BOTTOM edge sits just above the top of the inventory addon.
        // This keeps the button box visually attached to the inventory, with its bottom edge aligned.
        var windowHeight = 0f;
        var ctx = (ImGuiContext*)ImGui.GetCurrentContext();
        if (ctx != null)
        {
            // Estimate window height using previous frame or fallback to a default.
            windowHeight = ImGui.GetWindowHeight() > 0 ? ImGui.GetWindowHeight() : 52f;
        }
        // Position so the bottom of the overlay is just above the inventory window.
        var pos = new Vector2(anchorPos.X, anchorPos.Y - windowHeight - 2f); // 2px gap
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always);
        // Cap window width to the inventory addon's width; height is auto via AlwaysAutoResize.
        var maxW = MathF.Max(220f, anchorSize.X);
        ImGui.SetNextWindowSizeConstraints(new Vector2(220, 0), new Vector2(maxW, 9999));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(4);
    }

    /// <summary>
    /// Returns to the same line for the next button if it fits within the strip's width budget;
    /// otherwise lets ImGui flow onto the next line, producing a wrapping button row.
    /// </summary>
    private void WrapSameLine(float nextWidth = 92f)
    {
        var prevMaxX = ImGui.GetItemRectMax().X;
        var winStartX = ImGui.GetWindowPos().X;
        var winRightX = winStartX + ImGui.GetWindowWidth() - 8f; // 8 = right padding
        if (prevMaxX + 6f + nextWidth <= winRightX)
            ImGui.SameLine();
    }

    public override void Draw()
    {
        // Live "Layout Zones over the inventory" overlay was removed in 1.0.20 — it could not be
        // pixel-aligned reliably across the various inventory addons. The Layout Zones painter and
        // Apply Zones sort still use Config.VisualZoneLayout; only the live tinting is gone.

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

        // Smart Sort + per-rule + Undo
        if (ImGui.Button("Smart Sort##ov_smart", new Vector2(80, 0)))
            plugin.RunSmartSort();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Sort everything: Gear -> Bag1, Food/Medicine -> Bag2,\nMaterials -> Bag3, Crystals/Materia -> Bag4.");

        WrapSameLine(80);
        var rule = ResolveOverlayRule();
        if (rule != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, rule.GetColor() * new Vector4(1, 1, 1, 0.5f));
            if (ImGui.Button($"{rule.Name}##ov_rule", new Vector2(80, 0)))
                plugin.RunSingleRule(rule);
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Run only this rule:\n{rule.Name}");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("(no rule)##ov_rule_none", new Vector2(80, 0));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Pick a rule for the overlay button\nin Settings tab.");
        }

        WrapSameLine(60);
        if (plugin.CanUndo)
        {
            if (ImGui.SmallButton("Undo##ov_undo")) plugin.UndoLastSort();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Reverse the last sort.");
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.SmallButton("Undo##ov_undo_dis");
            ImGui.EndDisabled();
        }

        // Delete Junk + presets
        WrapSameLine(80);
        if (ImGui.Button("Delete Junk##ov_junk", new Vector2(80, 0)))
        {
            ImGui.OpenPopup("Confirm Delete Junk##ov");
            openJunkConfirmPopup = true;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Delete all vendor trash (white rarity items).");
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
            ImGui.Text("DELETES ALL ITEMS IN YOUR INVENTORY THAT HAVE A VENDOR PRICE UNDER \"MAX VENDOR PRICE FOR JUNK\" IN SETTINGS...");
            ImGui.Text("(Note: Currently an experimental feature and may fail, use at own risk.)");
            ImGui.PopStyleColor();
            ImGui.EndTooltip();
        }

        if (ImGui.BeginPopupModal("Confirm Delete Junk##ov", ref openJunkConfirmPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Are you sure you want to delete all junk items?");
            ImGui.Text("This matches items with a vendor price under your configured maximum.");
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
            ImGui.Text("This action cannot be undone!");
            ImGui.PopStyleColor();
            ImGui.Separator();
            if (ImGui.Button("Yes, delete junk", new Vector2(120, 0)))
            {
                plugin.DeleteJunk();
                ImGui.CloseCurrentPopup();
            }
            ImGui.SetItemDefaultFocus();
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(120, 0)))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        WrapSameLine(70);
        if (ImGui.SmallButton("Gatherer")) plugin.RunGathererSort();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Crystals & Materia top priority,\nmaterials next, rest in Bag2.");

        WrapSameLine(60);
        if (ImGui.SmallButton("Raider")) plugin.RunRaiderSort();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("High ilvl gear + consumables\nfor combat readiness.");

        WrapSameLine(60);
        if (ImGui.SmallButton("Hoarder")) plugin.RunHoarderSort();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Group by rarity (white/green/blue/purple)\nto find junk easily.");

        WrapSameLine(110);
        if (ImGui.SmallButton("Extract Materia##ov_materia")) plugin.ExtractMateria();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move all gear at 100% spiritbond to\nBag 1 for materia extraction.");

        WrapSameLine(100);
        if (ImGui.SmallButton("Merge Stacks##ov_merge")) plugin.MergeStacks();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Consolidate duplicate items\ninto single stacks.");

        WrapSameLine(95);
        if (ImGui.SmallButton("Apply Zones##ov_zones")) plugin.ApplyVisualZones();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rebuild inventory into your painted Layout Zones.");

        WrapSameLine(100);
        if (ImGui.SmallButton("Vendor Trash##ov_trash")) plugin.GroupVendorTrash();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Group all vendor trash (grey items)\ninto Bag 4 for easy discard.");

        WrapSameLine(40);
        if (ImGui.SmallButton("BA##ov_open")) plugin.ToggleMainUi();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open Bag Assistant");
    }

    private SortRule? ResolveOverlayRule()
    {
        if (Config.Rules.Count == 0) return null;
        var idx = Config.OverlayRuleIndex;
        if (idx < 0 || idx >= Config.Rules.Count) return null;
        var rule = Config.Rules[idx];
        return rule.Enabled ? rule : null;
    }

    // ─── Zone overlay ────────────────────────────────────────────────────────

    private static readonly (string Name, int BagIndex)[] ExpansionGrids =
    [
        ("InventoryGrid0E", 0),
        ("InventoryGrid1E", 1),
        ("InventoryGrid2E", 2),
        ("InventoryGrid3E", 3),
    ];

    /// <summary>
    /// Walks the live InventoryGrid* addons, reads each slot's actual screen rect, and paints
    /// zone tags directly over the matching cells.
    /// </summary>
    private void DrawZoneOverlay()
    {
        var drawList = ImGui.GetBackgroundDrawList();

        // InventoryExpansion: all four bags shown simultaneously (no tab needed for mapping).
        var expansion = (AddonInventoryExpansion*)gameGui.GetAddonByName("InventoryExpansion", 1).Address;
        if (expansion != null && expansion->AtkUnitBase.IsVisible)
        {
            foreach (var (gridName, bagIdx) in ExpansionGrids)
            {
                var grid = (AddonInventoryGrid*)gameGui.GetAddonByName(gridName, 1).Address;
                if (grid == null) continue;
                var gridBase = (AtkUnitBase*)grid;
                if (!gridBase->IsVisible) continue;
                PaintGrid(drawList, grid, bagIdx);
            }
            return;
        }

        // InventoryLarge: two grids visible. TabIndex 0 = bags 1+2, TabIndex 1 = bags 3+4.
        var large = (AddonInventoryLarge*)gameGui.GetAddonByName("InventoryLarge", 1).Address;
        if (large != null && ((AtkUnitBase*)large)->IsVisible)
        {
            var tab = large->TabIndex;
            int bag0 = tab == 0 ? 0 : 2;
            int bag1 = tab == 0 ? 1 : 3;

            var grid0 = (AddonInventoryGrid*)gameGui.GetAddonByName("InventoryGrid0", 1).Address;
            var grid1 = (AddonInventoryGrid*)gameGui.GetAddonByName("InventoryGrid1", 1).Address;
            if (grid0 != null && ((AtkUnitBase*)grid0)->IsVisible) PaintGrid(drawList, grid0, bag0);
            if (grid1 != null && ((AtkUnitBase*)grid1)->IsVisible) PaintGrid(drawList, grid1, bag1);
            return;
        }

        // Inventory (small): single visible grid for the active tab.
        var inv = (AddonInventory*)gameGui.GetAddonByName("Inventory", 1).Address;
        if (inv != null && ((AtkUnitBase*)inv)->IsVisible)
        {
            var bagIdx = Math.Clamp(inv->TabIndex, 0, 3);
            var grid = (AddonInventoryGrid*)gameGui.GetAddonByName("InventoryGrid", 1).Address;
            if (grid != null && ((AtkUnitBase*)grid)->IsVisible) PaintGrid(drawList, grid, bagIdx);
        }
    }

    private void PaintGrid(ImDrawListPtr drawList, AddonInventoryGrid* grid, int bagIndex)
    {
        var gridBase = (AtkUnitBase*)grid;
        var originX = gridBase->X;
        var originY = gridBase->Y;
        var scale = gridBase->Scale;

        for (int slotIndex = 0; slotIndex < 35; slotIndex++)
        {
            var slot = grid->Slots[slotIndex].Value;
            if (slot == null) continue;

            var node = ((AtkComponentBase*)slot)->OwnerNode;
            if (node == null) continue;
            var resNode = (AtkResNode*)node;

            // Slot rect in screen space. Slot nodes are direct children of the grid root, so
            // origin + (local pos * scale) yields the correct screen rect.
            var min = new Vector2(originX + resNode->X * scale, originY + resNode->Y * scale);
            var max = min + new Vector2(resNode->Width * scale, resNode->Height * scale);

            int globalIndex = bagIndex * 35 + slotIndex;
            if (globalIndex < 0 || globalIndex >= 140) continue;
            var tag = Config.VisualZoneLayout?[globalIndex] ?? "None";
            if (string.IsNullOrEmpty(tag) || tag == "None") continue;

            // Inset slightly so adjacent zone cells don't visually merge into a single blob.
            var inset = 1.5f * scale;
            var rectMin = new Vector2(min.X + inset, min.Y + inset);
            var rectMax = new Vector2(max.X - inset, max.Y - inset);
            var rounding = 4f * scale;

            var color = TagColor(tag);
            // Fill: very subtle wash so item icons remain fully readable.
            var fill = color;
            fill.W = Config.VisualZoneOverlayOpacity * 0.35f;
            drawList.AddRectFilled(rectMin, rectMax, ImGui.ColorConvertFloat4ToU32(fill), rounding);

            // Border: full requested opacity, gives a crisp zone outline.
            var border = color;
            border.W = MathF.Min(1f, Config.VisualZoneOverlayOpacity * 1.4f);
            drawList.AddRect(rectMin, rectMax, ImGui.ColorConvertFloat4ToU32(border), rounding, ImDrawFlags.None, 1.4f * scale);

            if (Config.ShowVisualZoneNumbers)
            {
                var text = (slotIndex + 1).ToString();
                var size = ImGui.CalcTextSize(text);
                var textPos = min + ((max - min) - size) * 0.5f;
                drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, Config.VisualZoneOverlayOpacity)), text);
            }
        }
    }

    private static Vector4 TagColor(string tag) => tag switch
    {
        "Gear" => new Vector4(0.2f, 0.6f, 1.0f, 1f),
        "Materia" => new Vector4(0.8f, 0.2f, 0.8f, 1f),
        "Consumables" => new Vector4(0.4f, 0.9f, 0.4f, 1f),
        "Crafting" => new Vector4(0.9f, 0.6f, 0.2f, 1f),
        "Gathering" => new Vector4(0.9f, 0.8f, 0.2f, 1f),
        "Crystals" => new Vector4(0.4f, 0.8f, 0.9f, 1f),
        "Junk" => new Vector4(0.5f, 0.5f, 0.5f, 1f),
        _ => new Vector4(0.2f, 0.2f, 0.2f, 1f),
    };

    /// <summary>
    /// Locate the parent inventory addon (Inventory / InventoryLarge / InventoryExpansion)
    /// to anchor the floating button strip above it.
    /// </summary>
    private bool TryGetInventoryAnchor(out Vector2 position, out Vector2 size)
    {
        position = Vector2.Zero;
        size = Vector2.Zero;

        foreach (var name in new[] { "Inventory", "InventoryLarge", "InventoryExpansion" })
        {
            var addonPtr = gameGui.GetAddonByName(name, 1);
            if (addonPtr.IsNull) continue;
            if (!addonPtr.IsVisible) continue;

            var w = MathF.Max(40f, addonPtr.ScaledWidth);
            var h = MathF.Max(40f, addonPtr.ScaledHeight);
            position = addonPtr.Position;
            size = new Vector2(w, h);
            return true;
        }
        return false;
    }
}
