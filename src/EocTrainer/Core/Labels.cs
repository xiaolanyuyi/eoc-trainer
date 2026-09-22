namespace EocTrainer.Core;

/// <summary>
/// Chinese labels for the values the trainer exposes. The game's own enum names
/// are always kept, both because they are the stable identifiers and because the
/// trainer has to send them back to the game unchanged.
/// </summary>
public static class Labels
{
    public static readonly Dictionary<string, string> AttributeNames = new()
    {
        ["Strength"] = "力量",
        ["Finesse"] = "敏捷",
        ["Intelligence"] = "智力",
        ["Constitution"] = "体质",
        ["Memory"] = "记忆",
        ["Wits"] = "智慧",
    };

    /// <summary>Abilities the character sheet actually offers, grouped for the UI.</summary>
    public static readonly Dictionary<string, (string Label, string Group)> AbilityNames = new()
    {
        ["WarriorLore"] = ("战争", "战斗能力"),
        ["RangerLore"] = ("猎人", "战斗能力"),
        ["RogueLore"] = ("恶棍", "战斗能力"),
        ["FireSpecialist"] = ("火系专精", "战斗能力"),
        ["WaterSpecialist"] = ("水系专精", "战斗能力"),
        ["AirSpecialist"] = ("气系专精", "战斗能力"),
        ["EarthSpecialist"] = ("土系专精", "战斗能力"),
        ["Necromancy"] = ("死灵法术", "战斗能力"),
        ["Polymorph"] = ("变形术", "战斗能力"),
        ["Summoning"] = ("召唤术", "战斗能力"),
        ["Sourcery"] = ("源力", "战斗能力"),
        ["DualWielding"] = ("双持武器", "战斗能力"),
        ["TwoHanded"] = ("双手武器", "战斗能力"),
        ["Ranged"] = ("远程武器", "战斗能力"),
        ["Shield"] = ("盾牌", "战斗能力"),
        ["SingleHanded"] = ("单手武器", "战斗能力"),
        ["Leadership"] = ("领导力", "战斗能力"),
        ["Barter"] = ("交易术", "民事能力"),
        ["Luck"] = ("幸运", "民事能力"),
        ["Loremaster"] = ("博学", "民事能力"),
        ["Persuasion"] = ("劝说", "民事能力"),
        ["Sneaking"] = ("潜行", "民事能力"),
        ["Telekinesis"] = ("念力", "民事能力"),
        ["Thievery"] = ("盗窃", "民事能力"),
    };

    /// <summary>Talents that show up on the character sheet.</summary>
    public static readonly Dictionary<string, string> TalentNames = new()
    {
        ["AttackOfOpportunity"] = "借机攻击",
        ["Backstab"] = "背刺",
        ["Bully"] = "欺凌弱者",
        ["ComebackKid"] = "重整旗鼓",
        ["Courageous"] = "勇敢",
        ["Demon"] = "恶魔",
        ["ElementalAffinity"] = "元素亲和",
        ["ElementalRanger"] = "元素游侠",
        ["Escapist"] = "逃脱大师",
        ["FaroutDude"] = "远程施法",
        ["FiveStarRestaurant"] = "五星大厨",
        ["Leech"] = "吸血",
        ["LightningRod"] = "避雷针",
        ["LoneWolf"] = "独狼",
        ["MrKnowItAll"] = "万事通",
        ["Politician"] = "政治家",
        ["Raistlin"] = "全才",
        ["StandYourGround"] = "坚守阵地",
        ["WalkItOff"] = "活动筋骨",
        ["WeatherProof"] = "风雨无阻",
        ["WhatARush"] = "急躁",
        ["Zombie"] = "亡灵",
        ["AnimalEmpathy"] = "动物之友",
        ["Scientist"] = "科学家",
        ["IceKing"] = "冰雪之王",
        ["GoldenMage"] = "黄金法师",
        ["DuckDuckGoose"] = "躲闪",
        ["BiggerAndBetter"] = "更大更好",
        ["Executioner"] = "刽子手",
        ["ThePawn"] = "棋子",
        ["Torturer"] = "折磨者",
        ["Unstable"] = "不稳定",
        ["Hothead"] = "火爆脾气",
        ["ParryMaster"] = "格挡大师",
        ["PictureOfHealth"] = "健康体魄",
        ["Slingshot"] = "弹弓",
        ["Ambidextrous"] = "双手灵巧",
        ["IronSkin"] = "铁皮",
        ["Stench"] = "恶臭",
        ["GlassCannon"] = "玻璃大炮",
        ["SurpriseAttack"] = "突袭",
        ["LightStep"] = "轻步",
        ["StandYourGround2"] = "坚守阵地",
    };

    /// <summary>Talent entries the enum carries but the character sheet never offers.</summary>
    /// <remarks>
    /// Kept as a reference list: entries in here are internal / legacy values that
    /// the game itself never shows in the character sheet. The UI hides every
    /// talent that has no Chinese label, which covers this list and anything else
    /// the enum grows in a later build.
    /// </remarks>
    public static readonly HashSet<string> LegacyTalents = new(StringComparer.Ordinal)
    {
        "ItemMovement", "ItemCreation", "Flanking", "Trade", "Lockpick",
        "ChanceToHitRanged", "ChanceToHitMelee", "Damage", "ActionPoints", "ActionPoints2",
        "Criticals", "IncreasedArmor", "Sight", "ResistFear", "ResistKnockdown",
        "ResistStun", "ResistPoison", "ResistSilence", "ResistDead", "Carry",
        "Throwing", "Repair", "ExpGain", "ExtraStatPoints", "ExtraSkillPoints",
        "Durability", "Awareness", "Vitality", "FireSpells", "WaterSpells",
        "AirSpells", "EarthSpells", "Charm", "Intimidate", "Reason",
        "Luck", "Initiative", "InventoryAccess", "AvoidDetection",
        "ResurrectToFullHealth", "ElementalAffinity", "ResistDead2",
    };

    public static string Attribute(string name) =>        AttributeNames.TryGetValue(name, out var label) ? label : name;

    public static string Ability(string name) =>
        AbilityNames.TryGetValue(name, out var info) ? info.Label : name;

    public static string AbilityGroup(string name) =>
        AbilityNames.TryGetValue(name, out var info) ? info.Group : "其他（旧版/隐藏）";

    public static bool IsCuratedAbility(string name) => AbilityNames.ContainsKey(name);

    public static string Talent(string name) =>
        TalentNames.TryGetValue(name, out var label) ? $"{label} ({name})" : name;

    /// <summary>
    /// Talents without a known label are the enum's internal/legacy entries that
    /// never appear on the character sheet; they are hidden unless asked for.
    /// </summary>
    public static bool IsLegacyTalent(string name) => !TalentNames.ContainsKey(name);

    /// <summary>"力量 (Strength)" style label used in the grids.</summary>
    public static string AttributeEntry(string name) =>
        AttributeNames.TryGetValue(name, out var label) ? $"{label} ({name})" : name;

    public static string AbilityEntry(string name) =>
        AbilityNames.TryGetValue(name, out var info) ? $"{info.Label} ({name})" : name;
}
