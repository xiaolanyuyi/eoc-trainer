namespace EocTrainer.Core;

public static partial class GameEnums
{
    /// <summary>Unspent point pools, as understood by the mod.</summary>
    public static readonly (string Key, string Label)[] PointPools =
    {
        ("attribute", "属性点"),
        ("combatAbility", "战斗能力点"),
        ("civilAbility", "民事能力点"),
        ("talent", "天赋点"),
    };

    /// <summary>
    /// Stats accepted by <c>NRD_CharacterSetPermanentBoostInt</c>; these are
    /// stored in the savegame, unlike temporary buffs.
    /// </summary>
    public static readonly string[] PermanentBoosts =
    {
        "Strength", "Finesse", "Intelligence", "Constitution", "Memory", "Wits",
        "Accuracy", "Dodge", "CriticalChance", "Initiative", "Movement", "Sight", "Hearing",
        "Vitality", "VitalityBoost", "MagicPoints", "MaxResistance", "LifeSteal",
        "Armor", "MagicArmor", "ArmorBoost", "MagicArmorBoost",
        "PhysicalResistance", "PiercingResistance", "CorrosiveResistance", "MagicResistance",
        "FireResistance", "EarthResistance", "WaterResistance", "AirResistance",
        "PoisonResistance", "ShadowResistance", "CustomResistance",
        "DamageBoost", "ChanceToHitBoost", "RangeBoost", "APMaximum", "APStart", "APRecovery",
        "MaxSummons", "Weight", "Level", "Gain",
    };
}
