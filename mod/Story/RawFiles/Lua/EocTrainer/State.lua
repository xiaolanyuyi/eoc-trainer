--- EoC Trainer - collects a snapshot of the party and hands it to the app.

local ET = EocTrainer

--- Calls f(...) and returns its result, or nil plus the error message.
local try = ET.try

local function num(v)
    if type(v) == "number" then return math.floor(v) end
    return nil
end

--- Reads a field of the character's stat sheet, tolerating missing fields.
local function statField(stats, name)
    if stats == nil then return nil end
    local ok, value = pcall(function() return stats[name] end)
    if not ok then return nil end
    if type(value) == "number" then return math.floor(value) end
    if type(value) == "boolean" then return value end
    return nil
end

--- Raw PlayerUpgrade data: the values the trainer actually writes, plus the
--- unspent point pools. Index order of Attributes[] is
--- Strength, Finesse, Intelligence, Constitution, Memory, Wits (verified in game).
local UPGRADE_ATTRIBUTES = { "Strength", "Finesse", "Intelligence", "Constitution", "Memory", "Wits" }

local function readUpgrade(character)
    local upgrade = character.PlayerUpgrade
    if upgrade == nil then return nil end

    local attributes = {}
    for index, name in ipairs(UPGRADE_ATTRIBUTES) do
        attributes[name] = num(try(function() return upgrade.Attributes[index] end))
    end

    return {
        attributes = attributes,
        points = {
            attribute = num(try(function() return upgrade.AttributePoints end)),
            combatAbility = num(try(function() return upgrade.CombatAbilityPoints end)),
            civilAbility = num(try(function() return upgrade.CivilAbilityPoints end)),
            talent = num(try(function() return upgrade.TalentPoints end)),
        },
        isCustom = try(function() return upgrade.IsCustom end) == true,
    }
end

local function readPoints(guid)
    return {
        attribute = num(try(Osi.CharacterGetAttributePoints, guid)),
        combatAbility = num(try(Osi.CharacterGetAbilityPoints, guid)),
        civilAbility = num(try(Osi.CharacterGetCivilAbilityPoints, guid)),
        talent = num(try(Osi.CharacterGetTalentPoints, guid)),
    }
end

local function readName(character, guid)
    local pc = character and character.PlayerCustomData
    local name = pc and pc.Name
    if type(name) == "string" and #name > 0 then return name end

    local display = try(Osi.CharacterGetDisplayName, guid)
    if type(display) == "string" and #display > 0 then return display end

    return guid
end

local function snapshotCharacter(guid, hostGuid)
    local character = try(Ext.GetCharacter, guid)
    if character == nil then return nil end

    local stats = character.Stats
    local upgrade = readUpgrade(character)
    local entry = {
        guid = guid,
        name = readName(character, guid),
        isHost = (guid == hostGuid),
        dead = character.Dead == true,
        level = statField(stats, "Level"),
        experience = statField(stats, "Experience"),
        gold = num(try(Osi.CharacterGetGold, guid)),
        points = (upgrade and upgrade.points) or readPoints(guid),
        upgradeAttributes = upgrade and upgrade.attributes or nil,
        customUpgrade = upgrade and upgrade.isCustom or false,
        attributes = {},
        baseAttributes = {},
        abilities = {},
        baseAbilities = {},
        talents = {},
        vitals = {
            vitality = statField(stats, "CurrentVitality"),
            maxVitality = statField(stats, "MaxVitality"),
            armor = statField(stats, "CurrentArmor"),
            maxArmor = statField(stats, "MaxArmor"),
            magicArmor = statField(stats, "CurrentMagicArmor"),
            maxMagicArmor = statField(stats, "MaxMagicArmor"),
            ap = statField(stats, "CurrentAP"),
            maxAp = statField(stats, "APMaximum"),
            source = statField(stats, "MagicPoints"),
            maxSource = statField(stats, "MaxMp"),
        },
    }

    for _, name in ipairs(ET.ATTRIBUTES) do
        entry.attributes[name] = statField(stats, name)
        entry.baseAttributes[name] = statField(stats, "Base" .. name)
    end

    for _, name in ipairs(ET.ABILITIES) do
        entry.abilities[name] = statField(stats, name)
        entry.baseAbilities[name] = statField(stats, "Base" .. name)
    end

    for _, name in ipairs(ET.TALENTS) do
        if statField(stats, "TALENT_" .. name) then
            entry.talents[#entry.talents + 1] = name
        end
    end

    return entry
end

--- Player rows can appear twice: once as the plain character GUID and once as
--- "<TemplateName>_<guid>" (eg. S_Player_RedPrince_...). Both point at the same
--- character, so entries are collapsed by their trailing GUID.
local function identityOf(guid)
    local suffix = guid:match("([^_]+)$")
    return suffix or guid
end

--- Builds the full state document that gets written to state.json.
function ET.snapshot()
    local party = {}
    local hostGuid = try(Osi.CharacterGetHostCharacter)
    local byIdentity = {}
    local order = {}

    local okRows, rows = pcall(function() return Osi.DB_IsPlayer:Get(nil) end)
    if okRows and type(rows) == "table" then
        for _, row in ipairs(rows) do
            local guid = row[1]
            if type(guid) == "string" and #guid > 0 then
                local key = identityOf(guid)
                local existing = byIdentity[key]
                -- Prefer the GUID the game itself hands out for the host.
                if existing == nil or (guid == hostGuid and existing ~= hostGuid) then
                    if existing == nil then order[#order + 1] = key end
                    byIdentity[key] = guid
                end
            end
        end
    end

    -- The host character should always be present, even when the IsPlayer
    -- database is empty or unavailable (eg. during a restricted context).
    if type(hostGuid) == "string" and #hostGuid > 0 then
        local key = identityOf(hostGuid)
        if byIdentity[key] == nil then
            order[#order + 1] = key
            byIdentity[key] = hostGuid
        end
    end

    for _, key in ipairs(order) do
        local entry = snapshotCharacter(byIdentity[key], hostGuid)
        if entry then party[#party + 1] = entry end
    end

    -- host first
    table.sort(party, function(a, b)
        if a.isHost == b.isHost then return false end
        return a.isHost
    end)

    return {
        version = ET.VERSION,
        bridge = true,
        inGame = true,
        acked = true,
        seq = ET.seq,
        token = ET.token,
        timestamp = ET.now(),
        ok = true,
        errors = ET.lastErrors,
        results = ET.lastOpResults or {},
        party = party,
        debug = {
            extenderVersion = ET.extenderVersion,
            gameVersion = ET.gameVersion,
        },
    }
end
