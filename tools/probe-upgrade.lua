-- Probe for the development channel: dumps the raw PlayerUpgrade structure.
local character = Ext.GetCharacter(CharacterGetHostCharacter())
if character == nil then return "no host character" end

local upgrade = character.PlayerUpgrade
if upgrade == nil then return "no PlayerUpgrade" end

local parts = {}

local okCount, count = pcall(function() return #upgrade.Attributes end)
parts[#parts + 1] = "attributes.count=" .. tostring(okCount and count or "ERR")

if okCount and type(count) == "number" then
    for index = 1, count do
        local ok, value = pcall(function() return upgrade.Attributes[index] end)
        parts[#parts + 1] = string.format("[%d]=%s", index, tostring(ok and value or "ERR"))
    end
end

local okAb, abCount = pcall(function() return #upgrade.Abilities end)
parts[#parts + 1] = "abilities.count=" .. tostring(okAb and abCount or "ERR")

for _, field in ipairs({ "AttributePoints", "CombatAbilityPoints", "CivilAbilityPoints", "TalentPoints", "IsCustom" }) do
    local ok, value = pcall(function() return upgrade[field] end)
    parts[#parts + 1] = string.format("%s=%s", field, tostring(ok and value or "ERR"))
end

local stats = character.Stats
for _, field in ipairs({ "Strength", "BaseStrength", "Finesse", "BaseFinesse" }) do
    local ok, value = pcall(function() return stats[field] end)
    parts[#parts + 1] = string.format("stats.%s=%s", field, tostring(ok and value or "ERR"))
end

return table.concat(parts, " ")
