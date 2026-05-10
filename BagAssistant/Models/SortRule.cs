using System;
using System.Collections.Generic;
using System.Numerics;

namespace BagAssistant;

/// <summary>How a rule decides where to place a matching item.</summary>
public enum SortTarget
{
    /// <summary>Place in any free slot of the included bags, in bag order then slot order.</summary>
    AnyFreeSlot,
    /// <summary>Place into one specific bag (Bag1..Bag4) in slot order.</summary>
    SpecificBag,
}

public enum HQMatch { Any, HQOnly, NQOnly }
public enum BoolMatch { Any, Yes, No }

/// <summary>One inventory-sort rule. The first rule (top of the list) whose filter matches an item wins.</summary>
public sealed class SortRule
{
    public string Name { get; set; } = "New Rule";
    public bool Enabled { get; set; } = true;

    /// <summary>Optional accent color used in the editor for visual grouping. RGB 0..1.</summary>
    public float DisplayColorR { get; set; } = 0.4f;
    public float DisplayColorG { get; set; } = 0.7f;
    public float DisplayColorB { get; set; } = 1.0f;

    // ─── Target ───────────────────────────────────────────────────────────────
    public SortTarget Target { get; set; } = SortTarget.AnyFreeSlot;
    /// <summary>Index 0..3 for SpecificBag (Inventory1..Inventory4).</summary>
    public int TargetBagIndex { get; set; } = 0;

    // ─── Filters ──────────────────────────────────────────────────────────────
    /// <summary>ItemUICategory row IDs that match (empty = any category).</summary>
    public List<uint> ItemUICategories { get; set; } = new();

    /// <summary>Allowed rarity values (1=white, 2=green, 3=blue, 4=purple, 7=pink). Empty = any.</summary>
    public List<byte> Rarities { get; set; } = new();

    /// <summary>Equip level range (inclusive). 0/0 disables.</summary>
    public int MinEquipLevel { get; set; } = 0;
    public int MaxEquipLevel { get; set; } = 0;

    /// <summary>Item level range (inclusive). 0/0 disables.</summary>
    public int MinItemLevel { get; set; } = 0;
    public int MaxItemLevel { get; set; } = 0;

    /// <summary>ClassJob abbreviations the item must support (e.g. "PLD"). Empty = any.</summary>
    public List<string> ClassJobs { get; set; } = new();

    public HQMatch HQ { get; set; } = HQMatch.Any;
    public BoolMatch Untradeable { get; set; } = BoolMatch.Any;
    public BoolMatch Collectable { get; set; } = BoolMatch.Any;
    public BoolMatch Stackable { get; set; } = BoolMatch.Any;
    public BoolMatch Equippable { get; set; } = BoolMatch.Any;

    /// <summary>Substring (case-insensitive) the item name must contain. Empty = ignored.</summary>
    public string NameContains { get; set; } = string.Empty;

    /// <summary>Vendor sale price range (gil). 0/0 disables.</summary>
    public uint MinVendorPrice { get; set; } = 0;
    public uint MaxVendorPrice { get; set; } = 0;

    /// <summary>Whitelist of specific item IDs that match (empty = ignore this filter).</summary>
    public List<uint> ItemIdWhitelist { get; set; } = new();

    /// <summary>Blacklist of specific item IDs that never match (empty = ignore).</summary>
    public List<uint> ItemIdBlacklist { get; set; } = new();

    public Vector4 GetColor() => new(DisplayColorR, DisplayColorG, DisplayColorB, 1f);
}
