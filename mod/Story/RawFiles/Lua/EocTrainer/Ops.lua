--- EoC Trainer - the actual stat operations.
---
--- Every operation returns nil on success, a string when it failed, or a value
--- for the "probe" style operations.

local ET = EocTrainer
local try = ET.try

local ATTRIBUTES = {}
for _, name in ipairs(ET.ATTRIBUTES) do ATTRIBUTES[name] = true end

--- Base attribute / ability / talent changes are only replicated to the client
--- when a dummy upgrade call is made, see the extender documentation.
local function forceUpgradeSync(guid)
    try(Osi.CharacterAddCivilAbilityPoint, guid, 0)
end

local function targetOf(op, cmd)
    local guid = op.target
    if type(guid) ~= "string" or #guid == 0 then
        guid = cmd.host
    end
    if type(guid) ~= "string" or #guid == 0 then
        guid = try(Osi.CharacterGetHostCharacter)
    end
    return guid
end

local ops = {}

--- Reads one of the unspent point pools, or nil when it cannot be read.
local function readPool(kind, guid)
    local query
    if kind == "attribute" then query = Osi.CharacterGetAttributePoints
    elseif kind == "combatAbility" then query = Osi.CharacterGetAbilityPoints
    elseif kind == "civilAbility" then query = Osi.CharacterGetCivilAbilityPoints
    elseif kind == "talent" then query = Osi.CharacterGetTalentPoints
    else return nil end
    return tonumber(try(query, guid))
end

--- Applies "add N" through an Osiris call.
---
--- The exact arity of these calls differs between game builds, so the two
--- argument form is tried first and a one-by-one fallback keeps the trainer
--- working even when the second parameter is not supported.
local function applyDelta(fn, guid, amount)
    if amount == 0 then return true end

    local ok = pcall(fn, guid, amount)
    if ok then return true end

    local step = amount > 0 and 1 or -1
    for _ = 1, math.abs(amount) do
        local okSingle = pcall(fn, guid, step)
        if not okSingle then
            okSingle = pcall(fn, guid)
            if not okSingle then return false, "调用被拒绝（参数不匹配）" end
        end
    end

    return true
end

ops.addPoint = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local amount = math.floor(tonumber(op.amount) or 1)
    local kind = op.kind
    local fn
    if kind == "attribute" then fn = Osi.CharacterAddAttributePoint
    elseif kind == "combatAbility" then fn = Osi.CharacterAddAbilityPoint
    elseif kind == "civilAbility" then fn = Osi.CharacterAddCivilAbilityPoint
    elseif kind == "talent" then fn = Osi.CharacterAddTalentPoint
    elseif kind == "source" then fn = Osi.CharacterAddSourcePoints
    elseif kind == "actionPoint" then fn = Osi.CharacterAddActionPoints
    else return "unknown point kind '" .. tostring(kind) .. "'" end

    local before = readPool(kind, guid)
    local ok, err = applyDelta(fn, guid, amount)
    if not ok then return err end

    forceUpgradeSync(guid)

    local after = readPool(kind, guid)
    if before ~= nil and after ~= nil and after == before and amount > 0 then
        return string.format("已调用 %s，但%s点数没有变化（该角色可能不是玩家角色，或数值已到上限）",
            kind, kind)
    end

    return nil
end

ops.setAttribute = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end
    if not ATTRIBUTES[op.name] then return "unknown attribute '" .. tostring(op.name) .. "'" end

    local value = math.floor(tonumber(op.value) or 0)
    local ok, err = pcall(Osi.NRD_PlayerSetBaseAttribute, guid, op.name, value)
    if not ok then return tostring(err) end
    forceUpgradeSync(guid)
    return nil
end

ops.setAbility = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local value = math.floor(tonumber(op.value) or 0)
    local ok, err = pcall(Osi.NRD_PlayerSetBaseAbility, guid, op.name, value)
    if not ok then return tostring(err) end
    forceUpgradeSync(guid)
    return nil
end

ops.setTalent = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local value = (tonumber(op.value) or 0) ~= 0 and 1 or 0
    local ok, err = pcall(Osi.NRD_PlayerSetBaseTalent, guid, op.name, value)
    if not ok then return tostring(err) end
    forceUpgradeSync(guid)
    return nil
end

ops.addGold = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local amount = math.floor(tonumber(op.amount) or 0)
    local before = tonumber(try(Osi.CharacterGetGold, guid))
    local ok, err = applyDelta(Osi.CharacterAddGold, guid, amount)
    if not ok then return err end

    local after = tonumber(try(Osi.CharacterGetGold, guid))
    if before ~= nil and after ~= nil and after == before and amount > 0 then
        return "已调用 CharacterAddGold，但金币没有变化"
    end

    return nil
end

ops.setGold = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local want = math.floor(tonumber(op.value) or 0)
    local current = tonumber(try(Osi.CharacterGetGold, guid))
    if current == nil then return "current gold could not be read" end

    return ops.addGold({ target = guid, amount = want - current }, cmd)
end

--- Sets one of the three "current" pool values the extender lets us write.
ops.setStat = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local allowed = { CurrentVitality = true, CurrentArmor = true, CurrentMagicArmor = true }
    if not allowed[op.name] then return "stat '" .. tostring(op.name) .. "' is not writable" end

    local value = math.floor(tonumber(op.value) or 0)
    local ok, err = pcall(Osi.NRD_CharacterSetStatInt, guid, op.name, value)
    if not ok then return tostring(err) end
    return nil
end

ops.permanentBoost = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local value = math.floor(tonumber(op.value) or 0)
    local ok, err = pcall(Osi.NRD_CharacterSetPermanentBoostInt, guid, op.name, value)
    if not ok then return tostring(err) end
    try(Osi.CharacterAddAttribute, guid, "Dummy", 0)
    return nil
end

ops.permanentBoostTalent = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local value = (tonumber(op.value) or 0) ~= 0 and 1 or 0
    local ok, err = pcall(Osi.NRD_CharacterSetPermanentBoostTalent, guid, op.name, value)
    if not ok then return tostring(err) end
    try(Osi.CharacterAddAttribute, guid, "Dummy", 0)
    return nil
end

ops.resetCooldowns = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end
    local ok, err = pcall(Osi.CharacterResetCooldowns, guid)
    if not ok then return tostring(err) end
    return nil
end

ops.resurrect = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end
    local ok, err = pcall(Osi.CharacterResurrect, guid)
    if not ok then return tostring(err) end
    return nil
end

--- Debug helper: reads arbitrary stat sheet fields and reports them back.
ops.probe = function(op, cmd)
    local guid = targetOf(op, cmd)
    if guid == nil then return "no target character" end

    local character = try(Ext.GetCharacter, guid)
    if character == nil then return "character not found: " .. tostring(guid) end

    local stats = character.Stats
    if stats == nil then return "character has no stat sheet" end

    local out = {}
    for _, field in ipairs(op.fields or {}) do
        local value = try(function() return stats[field] end)
        out[#out + 1] = string.format("%s=%s", tostring(field), tostring(value))
    end
    return { text = table.concat(out, ", ") }
end

--- Debug helper: resolves localization keys, used to find the game's own
--- display names for attributes / abilities / talents.
ops.probeL10N = function(op, cmd)
    local out = {}
    for _, key in ipairs(op.keys or {}) do
        local value = try(Ext.L10N.GetTranslatedStringFromKey, key)
        out[#out + 1] = string.format("%s=%s", tostring(key), tostring(value))
    end
    return { text = table.concat(out, ", ") }
end

--- Escape hatch for experiments; evaluates a Lua snippet in the server state.
ops.eval = function(op, cmd)
    local chunk = Ext.Utils.LoadString(tostring(op.code or ""))
    if chunk == nil then return "snippet could not be compiled" end
    local ok, value = pcall(chunk)
    if not ok then return "eval failed: " .. tostring(value) end
    return { text = tostring(value) }
end

local function message(err)
    if type(err) == "table" then
        return tostring(err.message or err.code or "error")
    end
    return tostring(err)
end

--- Executes every operation of a command document.
--- Returns an array of human readable error messages (empty when all went well).
function ET.runOps(cmd)
    local errors = {}
    local results = {}
    local list = type(cmd.ops) == "table" and cmd.ops or {}

    for index, op in ipairs(list) do
        if type(op) ~= "table" or type(op.op) ~= "string" then
            errors[#errors + 1] = string.format("operation #%d is malformed", index)
        else
            local impl = ops[op.op]
            if impl == nil then
                errors[#errors + 1] = "unknown operation '" .. tostring(op.op) .. "'"
            else
                local ok, result = pcall(impl, op, cmd)
                if not ok then
                    errors[#errors + 1] = string.format("%s failed: %s", op.op, message(result))
                elseif type(result) == "string" then
                    errors[#errors + 1] = result
                elseif type(result) == "table" then
                    local text = result.text
                    if text == nil then
                        local okJson, json = pcall(Ext.Json.Stringify, result)
                        text = okJson and json or tostring(result)
                    end
                    results[#results + 1] = { op = op.op, target = op.target, value = text }
                end
            end
        end
    end

    ET.lastOpResults = results
    return errors
end
