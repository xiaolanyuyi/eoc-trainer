-- Development probe: dumps the raw ability array and the point pools.
local character = Ext.GetCharacter(CharacterGetHostCharacter())
if character == nil then return "no host character" end

local upgrade = character.PlayerUpgrade
if upgrade == nil then return "no PlayerUpgrade" end

local stats = character.Stats
local parts = {}

-- where does a distinctive value land in the ability array?
local okCount, count = pcall(function() return #upgrade.Abilities end)
parts[#parts + 1] = "abilities.count=" .. tostring(okCount and count or "ERR")

if okCount and type(count) == "number" then
    for index = 1, count do
        local ok, value = pcall(function() return upgrade.Abilities[index] end)
        if ok and value ~= nil and value ~= 0 then
            parts[#parts + 1] = string.format("[%d]=%s", index, tostring(value))
        end
    end
end

parts[#parts + 1] = "sheet.WarriorLore=" .. tostring(stats.WarriorLore)
parts[#parts + 1] = "sheet.BaseWarriorLore=" .. tostring(stats.BaseWarriorLore)
parts[#parts + 1] = "sheet.WarriorLore2=" .. tostring(stats.WarriorLore)

for _, field in ipairs({ "AttributePoints", "CombatAbilityPoints", "CivilAbilityPoints", "TalentPoints" }) do
    local ok, value = pcall(function() return upgrade[field] end)
    parts[#parts + 1] = string.format("%s=%s", field, tostring(ok and value or "ERR"))
end

return table.concat(parts, " ")
