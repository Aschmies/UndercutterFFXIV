using BagAssistant.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace BagAssistant.Windows;

public sealed class BagAssistantWindow : Window, IDisposable
{
    private readonly BagAssistantPlugin plugin;
    private readonly IDataManager dataManager;
    private Configuration Config => plugin.Configuration;

    private string statusMessage = string.Empty;
    private List<InventoryItemInfo> previewItems = new();
    private bool confirmingSort;

    // Sort execution queue (one move per tick, paced).
    private readonly Queue<SortPlanEntry> moveQueue = new();
    private int queueTotal;
    private readonly Stopwatch queueTimer = new();
    private int nextDelayMs;
    private static readonly Random Rng = new();

    // Cached UI category list.
    private readonly List<(uint RowId, string Name)> uiCategoryCache = new();
    private string uiCategoryFilter = string.Empty;

    private static readonly (string Abbr, string Group)[] AllJobs =
    [
        ("PLD","Tank"),("WAR","Tank"),("DRK","Tank"),("GNB","Tank"),
        ("WHM","Healer"),("SCH","Healer"),("AST","Healer"),("SGE","Healer"),
        ("MNK","Melee"),("DRG","Melee"),("NIN","Melee"),("SAM","Melee"),("RPR","Melee"),("VPR","Melee"),
        ("BRD","Phys Range"),("MCH","Phys Range"),("DNC","Phys Range"),
        ("BLM","Mag Range"),("SMN","Mag Range"),("RDM","Mag Range"),("PCT","Mag Range"),("BLU","Mag Range"),
        ("CRP","Crafter"),("BSM","Crafter"),("ARM","Crafter"),("GSM","Crafter"),("LTW","Crafter"),("WVR","Crafter"),("ALC","Crafter"),("CUL","Crafter"),
        ("MIN","Gatherer"),("BTN","Gatherer"),("FSH","Gatherer"),
    ];

    private static readonly (string Label, byte Value)[] RarityChoices =
    [
        ("White (1)", 1), ("Green (2)", 2), ("Blue (3)", 3), ("Purple (4)", 4), ("Pink (7)", 7),
    ];

    public BagAssistantWindow(BagAssistantPlugin plugin, IDataManager dataManager)
        : base("Bag Assistant##BagAssistant")
    {
        this.plugin = plugin;
        this.dataManager = dataManager;
        IsOpen = false;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(640, 520),
            MaximumSize = new Vector2(1200, 1000),
        };
        BuildCategoryCache();
    }

    public void Dispose() { }

    private void BuildCategoryCache()
    {
        var sheet = dataManager.GetExcelSheet<ItemUICategory>();
        if (sheet == null) return;
        foreach (var row in sheet)
        {
            var name = row.Name.ToString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            uiCategoryCache.Add((row.RowId, name));
        }
        uiCategoryCache.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    public override void Draw()
    {
        TickMoveQueue();

        if (ImGui.BeginTabBar("##BAQTabs"))
        {
            if (ImGui.BeginTabItem("Rules"))
            {
                DrawRulesTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Run / Preview"))
            {
                DrawRunTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Help"))
            {
                DrawHelpTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        if (!string.IsNullOrEmpty(statusMessage))
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 1f, 1f), statusMessage);
        }
    }

    // ─── Tab: Rules ──────────────────────────────────────────────────────────

    private void DrawRulesTab()
    {
        ImGui.TextWrapped("Rules are evaluated top-to-bottom. The first rule whose filters match an item wins. Use the arrows to reorder.");
        ImGui.Spacing();

        if (ImGui.Button("Add Rule"))
        {
            Config.Rules.Add(new SortRule { Name = $"Rule {Config.Rules.Count + 1}" });
            Config.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Add Common Presets"))
        {
            AddPresetRules();
            Config.Save();
        }

        ImGui.Spacing();

        for (var i = 0; i < Config.Rules.Count; i++)
        {
            var rule = Config.Rules[i];
            ImGui.PushID($"rule_{i}");
            ImGui.PushStyleColor(ImGuiCol.Header, rule.GetColor() * new Vector4(1, 1, 1, 0.35f));

            var header = $"{(rule.Enabled ? "[on] " : "[off] ")}{rule.Name} -> {DescribeTarget(rule)}";
            var open = ImGui.CollapsingHeader($"{header}##header_{i}");
            ImGui.PopStyleColor();

            if (open)
            {
                DrawRuleEditor(rule, i);
            }

            ImGui.PopID();
        }
    }

    private static string DescribeTarget(SortRule rule)
    {
        return rule.Target switch
        {
            SortTarget.SpecificBag => $"Bag {rule.TargetBagIndex + 1}",
            _ => "(classify only)",
        };
    }

    private void DrawRuleEditor(SortRule rule, int index)
    {
        // Header row: name, enabled, reorder, delete
        var name = rule.Name;
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Name##name", ref name, 64))
        {
            rule.Name = name;
            Config.Save();
        }
        ImGui.SameLine();
        var enabled = rule.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            rule.Enabled = enabled;
            Config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("^") && index > 0)
        {
            (Config.Rules[index - 1], Config.Rules[index]) = (Config.Rules[index], Config.Rules[index - 1]);
            Config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("v") && index < Config.Rules.Count - 1)
        {
            (Config.Rules[index + 1], Config.Rules[index]) = (Config.Rules[index], Config.Rules[index + 1]);
            Config.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Delete"))
        {
            Config.Rules.RemoveAt(index);
            Config.Save();
            return;
        }

        var color = new Vector3(rule.DisplayColorR, rule.DisplayColorG, rule.DisplayColorB);
        if (ImGui.ColorEdit3("Color", ref color, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel))
        {
            rule.DisplayColorR = color.X;
            rule.DisplayColorG = color.Y;
            rule.DisplayColorB = color.Z;
            Config.Save();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted("Accent");

        ImGui.Separator();

        // Target
        ImGui.TextUnformatted("Destination");
        var targetIdx = (int)rule.Target;
        if (ImGui.Combo("Target##target", ref targetIdx, "Any free slot (classify only)\0Specific bag\0"))
        {
            rule.Target = (SortTarget)targetIdx;
            Config.Save();
        }
        if (rule.Target == SortTarget.SpecificBag)
        {
            ImGui.SameLine();
            var bagIdx = rule.TargetBagIndex;
            ImGui.SetNextItemWidth(120);
            if (ImGui.Combo("Bag##bag", ref bagIdx, "Bag 1\0Bag 2\0Bag 3\0Bag 4\0"))
            {
                rule.TargetBagIndex = bagIdx;
                Config.Save();
            }
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Filters (all selected criteria must match — empty = any)");
        ImGui.Spacing();

        DrawCategoryFilter(rule);
        DrawRarityFilter(rule);
        DrawLevelFilters(rule);
        DrawJobFilter(rule);
        DrawFlagsFilter(rule);
        DrawNameFilter(rule);
        DrawPriceFilter(rule);
        DrawIdListFilters(rule);

        ImGui.Spacing();
        ImGui.Separator();
    }

    private void DrawCategoryFilter(SortRule rule)
    {
        if (!ImGui.TreeNode("Item Category##cat")) return;

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##catfilter", "Search categories", ref uiCategoryFilter, 64);

        var pending = uiCategoryCache
            .Where(c => string.IsNullOrEmpty(uiCategoryFilter)
                        || c.Name.Contains(uiCategoryFilter, StringComparison.OrdinalIgnoreCase))
            .Take(80);

        ImGui.BeginChild("##catlist", new Vector2(0, 160), true);
        foreach (var (rowId, catName) in pending)
        {
            var on = rule.ItemUICategories.Contains(rowId);
            if (ImGui.Checkbox($"{catName}##c{rowId}", ref on))
            {
                if (on) rule.ItemUICategories.Add(rowId);
                else rule.ItemUICategories.Remove(rowId);
                Config.Save();
            }
        }
        ImGui.EndChild();

        if (rule.ItemUICategories.Count > 0)
        {
            ImGui.TextDisabled($"Selected: {rule.ItemUICategories.Count}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear##clearcat"))
            {
                rule.ItemUICategories.Clear();
                Config.Save();
            }
        }
        ImGui.TreePop();
    }

    private void DrawRarityFilter(SortRule rule)
    {
        if (!ImGui.TreeNode("Rarity##rarity")) return;
        foreach (var (label, val) in RarityChoices)
        {
            var on = rule.Rarities.Contains(val);
            if (ImGui.Checkbox($"{label}##rar{val}", ref on))
            {
                if (on) rule.Rarities.Add(val);
                else rule.Rarities.Remove(val);
                Config.Save();
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();
        ImGui.TreePop();
    }

    private void DrawLevelFilters(SortRule rule)
    {
        if (!ImGui.TreeNode("Equip Level / Item Level##levels")) return;

        var minE = rule.MinEquipLevel;
        var maxE = rule.MaxEquipLevel;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputInt("Min eqp lvl##minel", ref minE)) { rule.MinEquipLevel = Math.Max(0, minE); Config.Save(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputInt("Max eqp lvl##maxel", ref maxE)) { rule.MaxEquipLevel = Math.Max(0, maxE); Config.Save(); }

        var minI = rule.MinItemLevel;
        var maxI = rule.MaxItemLevel;
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputInt("Min ilvl##minil", ref minI)) { rule.MinItemLevel = Math.Max(0, minI); Config.Save(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80);
        if (ImGui.InputInt("Max ilvl##maxil", ref maxI)) { rule.MaxItemLevel = Math.Max(0, maxI); Config.Save(); }

        ImGui.TextDisabled("(0 = unlimited / off)");
        ImGui.TreePop();
    }

    private void DrawJobFilter(SortRule rule)
    {
        if (!ImGui.TreeNode("Class / Job##jobs")) return;

        string? lastGroup = null;
        var col = 0;
        foreach (var (abbr, group) in AllJobs)
        {
            if (group != lastGroup)
            {
                if (lastGroup != null) ImGui.NewLine();
                ImGui.TextDisabled(group + ":");
                ImGui.SameLine();
                lastGroup = group;
                col = 0;
            }
            var on = rule.ClassJobs.Contains(abbr);
            if (ImGui.Checkbox($"{abbr}##j{abbr}", ref on))
            {
                if (on) rule.ClassJobs.Add(abbr);
                else rule.ClassJobs.Remove(abbr);
                Config.Save();
            }
            ImGui.SameLine();
            col++;
            if (col % 8 == 0) { ImGui.NewLine(); ImGui.Dummy(new Vector2(80, 0)); ImGui.SameLine(); }
        }
        ImGui.NewLine();
        ImGui.TreePop();
    }

    private void DrawFlagsFilter(SortRule rule)
    {
        if (!ImGui.TreeNode("Flags (HQ / Tradeable / Collectable / Stackable / Equippable)##flags")) return;

        var hq = (int)rule.HQ;
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo("HQ##hq", ref hq, "Any\0HQ only\0NQ only\0")) { rule.HQ = (HQMatch)hq; Config.Save(); }

        DrawBoolMatch("Untradeable", () => rule.Untradeable, x => rule.Untradeable = x);
        DrawBoolMatch("Collectable", () => rule.Collectable, x => rule.Collectable = x);
        DrawBoolMatch("Stackable", () => rule.Stackable, x => rule.Stackable = x);
        DrawBoolMatch("Equippable", () => rule.Equippable, x => rule.Equippable = x);

        ImGui.TreePop();
    }

    private void DrawBoolMatch(string label, Func<BoolMatch> get, Action<BoolMatch> set)
    {
        var v = (int)get();
        ImGui.SetNextItemWidth(160);
        if (ImGui.Combo($"{label}##{label}", ref v, "Any\0Yes\0No\0"))
        {
            set((BoolMatch)v);
            Config.Save();
        }
    }

    private void DrawNameFilter(SortRule rule)
    {
        if (!ImGui.TreeNode("Name##name")) return;
        var s = rule.NameContains;
        ImGui.SetNextItemWidth(280);
        if (ImGui.InputTextWithHint("Name contains##nc", "case-insensitive substring", ref s, 64))
        {
            rule.NameContains = s;
            Config.Save();
        }
        ImGui.TreePop();
    }

    private void DrawPriceFilter(SortRule rule)
    {
        if (!ImGui.TreeNode("Vendor Price##price")) return;
        var min = (int)rule.MinVendorPrice;
        var max = (int)rule.MaxVendorPrice;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Min gil##minp", ref min)) { rule.MinVendorPrice = (uint)Math.Max(0, min); Config.Save(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Max gil##maxp", ref max)) { rule.MaxVendorPrice = (uint)Math.Max(0, max); Config.Save(); }
        ImGui.TextDisabled("(0 = unlimited / off)");
        ImGui.TreePop();
    }

    private void DrawIdListFilters(SortRule rule)
    {
        if (!ImGui.TreeNode("Item ID Whitelist / Blacklist##ids")) return;
        DrawIdList("Whitelist", rule.ItemIdWhitelist);
        DrawIdList("Blacklist", rule.ItemIdBlacklist);
        ImGui.TextDisabled("Tip: comma-separated item IDs. Whitelist forces only those IDs to match; Blacklist excludes them.");
        ImGui.TreePop();
    }

    private void DrawIdList(string label, List<uint> list)
    {
        var current = string.Join(",", list);
        ImGui.SetNextItemWidth(360);
        if (ImGui.InputText($"{label}##idlist_{label}", ref current, 1024))
        {
            list.Clear();
            foreach (var token in current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (uint.TryParse(token, out var id)) list.Add(id);
            }
            Config.Save();
        }
    }

    // ─── Tab: Run / Preview ─────────────────────────────────────────────────

    private void DrawRunTab()
    {
        if (ImGui.Button("Preview Matches"))
        {
            previewItems = plugin.InventoryService.ScanBags(GetBagFlags());
            statusMessage = $"Scanned {previewItems.Count} item(s) across selected bags.";
        }

        ImGui.SameLine();
        var canSort = moveQueue.Count == 0 && Config.Rules.Any(r => r.Enabled && r.Target == SortTarget.SpecificBag);
        if (!canSort) ImGui.BeginDisabled();

        if (ImGui.Button(Config.RequireConfirmation && !confirmingSort ? "Sort Now..." : "Confirm Sort Now"))
        {
            if (Config.RequireConfirmation && !confirmingSort)
            {
                confirmingSort = true;
            }
            else
            {
                StartSort();
                confirmingSort = false;
            }
        }
        if (!canSort) ImGui.EndDisabled();

        if (confirmingSort)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel##cancelconfirm")) confirmingSort = false;
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), "Click 'Confirm Sort Now' to begin moving items.");
        }

        if (moveQueue.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Stop Sort"))
            {
                moveQueue.Clear();
                queueTimer.Stop();
                statusMessage = "Sort stopped by user.";
            }
        }

        ImGui.Separator();

        if (previewItems.Count == 0)
        {
            ImGui.TextDisabled("Click 'Preview Matches' to see how each item maps to your rules.");
            return;
        }

        if (ImGui.BeginTable("##preview", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 360)))
        {
            ImGui.TableSetupColumn("Item");
            ImGui.TableSetupColumn("Source");
            ImGui.TableSetupColumn("Rule");
            ImGui.TableSetupColumn("Destination");
            ImGui.TableSetupColumn("Action");
            ImGui.TableHeadersRow();

            foreach (var item in previewItems)
            {
                var matched = Config.Rules.FirstOrDefault(r => RuleMatcher.Matches(r, item));
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(item.Name + (item.IsHQ ? " [HQ]" : string.Empty));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{ContainerLabel(item.Container)}#{item.Slot + 1}");
                ImGui.TableNextColumn();
                if (matched != null)
                {
                    ImGui.TextColored(matched.GetColor(), matched.Name);
                }
                else
                {
                    ImGui.TextDisabled("(none)");
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(matched != null ? DescribeTarget(matched) : "—");
                ImGui.TableNextColumn();
                if (matched != null && matched.Target == SortTarget.SpecificBag)
                {
                    var destBag = InventoryService.PlayerBags[Math.Clamp(matched.TargetBagIndex, 0, 3)];
                    if (item.Container == destBag) ImGui.TextDisabled("in place");
                    else ImGui.TextColored(new Vector4(0.3f, 1f, 0.6f, 1f), "will move");
                }
                else
                {
                    ImGui.TextDisabled("classify");
                }
            }

            ImGui.EndTable();
        }
    }

    private static string ContainerLabel(FFXIVClientStructs.FFXIV.Client.Game.InventoryType t) => t switch
    {
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1 => "Bag1",
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2 => "Bag2",
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3 => "Bag3",
        FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4 => "Bag4",
        _ => t.ToString(),
    };

    private void StartSort()
    {
        var plan = plugin.SortRunner.BuildPlan(Config);
        if (plan.Count == 0)
        {
            statusMessage = "Nothing to do — every item is already in place (or no rule has a destination).";
            return;
        }
        foreach (var entry in plan) moveQueue.Enqueue(entry);
        queueTotal = plan.Count;
        nextDelayMs = 0;
        queueTimer.Restart();
        statusMessage = $"Sorting {queueTotal} item(s)...";
    }

    private void TickMoveQueue()
    {
        if (moveQueue.Count == 0) return;
        if (queueTimer.IsRunning && queueTimer.ElapsedMilliseconds < nextDelayMs) return;

        var entry = moveQueue.Dequeue();
        var (success, msg) = plugin.InventoryService.MoveOrSwap(entry.Item, entry.DestBag);
        var done = queueTotal - moveQueue.Count;

        if (moveQueue.Count == 0)
        {
            queueTimer.Stop();
            statusMessage = $"Sort complete: {done}/{queueTotal} processed.";
        }
        else
        {
            var min = Math.Max(20, Config.MoveDelayMinMs);
            var max = Math.Max(min + 1, Config.MoveDelayMaxMs);
            nextDelayMs = Rng.Next(min, max);
            queueTimer.Restart();
            statusMessage = $"Sorting... {done}/{queueTotal} (next in {nextDelayMs}ms)";
        }

        if (!success)
            statusMessage = $"[{done}/{queueTotal}] {msg}";
    }

    // ─── Tab: Settings ──────────────────────────────────────────────────────

    private void DrawSettingsTab()
    {
        ImGui.TextUnformatted("Bags considered when scanning and as move destinations:");
        DrawBagToggle("Bag 1", c => c.IncludeBag1, (c, v) => c.IncludeBag1 = v);
        DrawBagToggle("Bag 2", c => c.IncludeBag2, (c, v) => c.IncludeBag2 = v);
        DrawBagToggle("Bag 3", c => c.IncludeBag3, (c, v) => c.IncludeBag3 = v);
        DrawBagToggle("Bag 4", c => c.IncludeBag4, (c, v) => c.IncludeBag4 = v);

        ImGui.Separator();
        var confirm = Config.RequireConfirmation;
        if (ImGui.Checkbox("Require confirmation before sorting", ref confirm))
        {
            Config.RequireConfirmation = confirm;
            Config.Save();
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Move pacing (random delay per item, in ms — keeps the game happy):");
        var min = Config.MoveDelayMinMs;
        var max = Config.MoveDelayMaxMs;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Min ms##mindelay", ref min)) { Config.MoveDelayMinMs = Math.Max(20, min); Config.Save(); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Max ms##maxdelay", ref max)) { Config.MoveDelayMaxMs = Math.Max(Config.MoveDelayMinMs + 1, max); Config.Save(); }
    }

    private void DrawBagToggle(string label, Func<Configuration, bool> get, Action<Configuration, bool> set)
    {
        var v = get(Config);
        if (ImGui.Checkbox(label + "##" + label, ref v))
        {
            set(Config, v);
            Config.Save();
        }
    }

    // ─── Tab: Help ──────────────────────────────────────────────────────────

    private void DrawHelpTab()
    {
        ImGui.TextWrapped("Bag Assistant lets you build a stack of rules that decide where each inventory item belongs.");
        ImGui.Spacing();
        ImGui.TextWrapped("Workflow:");
        ImGui.BulletText("Add rules in the Rules tab. Each rule has filters (category, rarity, level, ilvl, job, HQ, name, price, etc.) and a destination bag.");
        ImGui.BulletText("Rules are evaluated top-to-bottom — the first matching rule wins. Reorder with the up/down arrows.");
        ImGui.BulletText("Use Run / Preview to see exactly which items will move where, then click Sort Now.");
        ImGui.BulletText("Items that don't match any rule are left where they are.");
        ImGui.Spacing();
        ImGui.TextWrapped("Tip: 'Add Common Presets' creates a starter set: HQ gear -> Bag1, materia/crystals -> Bag4, food/potions -> Bag3, everything else stays put.");
        ImGui.Spacing();
        ImGui.TextWrapped("Commands:");
        ImGui.BulletText("/bagassistant or /ba — open this window.");
    }

    private bool[] GetBagFlags() => new[] { Config.IncludeBag1, Config.IncludeBag2, Config.IncludeBag3, Config.IncludeBag4 };

    // ─── Presets ────────────────────────────────────────────────────────────

    private void AddPresetRules()
    {
        Config.Rules.Add(new SortRule
        {
            Name = "HQ Gear -> Bag 1",
            Target = SortTarget.SpecificBag,
            TargetBagIndex = 0,
            HQ = HQMatch.HQOnly,
            Equippable = BoolMatch.Yes,
            DisplayColorR = 1f, DisplayColorG = 0.8f, DisplayColorB = 0.2f,
        });
        Config.Rules.Add(new SortRule
        {
            Name = "Food / Medicine -> Bag 3",
            Target = SortTarget.SpecificBag,
            TargetBagIndex = 2,
            NameContains = string.Empty,
            // ItemUICategory rows: 46 = Meal, 44 = Medicine
            ItemUICategories = new() { 46, 44 },
            DisplayColorR = 0.4f, DisplayColorG = 1f, DisplayColorB = 0.6f,
        });
        Config.Rules.Add(new SortRule
        {
            Name = "Materia / Crystals -> Bag 4",
            Target = SortTarget.SpecificBag,
            TargetBagIndex = 3,
            // 59 = Materia, 63 = Crystal (Lumina rows; close enough — user can tweak)
            ItemUICategories = new() { 59, 63 },
            DisplayColorR = 0.7f, DisplayColorG = 0.5f, DisplayColorB = 1f,
        });
    }
}
