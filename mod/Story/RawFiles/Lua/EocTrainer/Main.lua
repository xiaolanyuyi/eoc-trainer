--- EoC Trainer - tick loop, command dispatch and session bookkeeping.

local ET = EocTrainer

ET.inSession = false
ET.lastPoll = 0
ET.lastHeartbeat = 0
ET.lastErrors = {}
ET.seq = 0
ET.token = ""
ET.ackToken = ""
ET.ackSeq = 0

function ET.now()
    local ok, t = pcall(Ext.MonotonicTime)
    if ok and type(t) == "number" then return t end
    return 0
end

local function describeCommand(cmd)
    local count = 0
    if type(cmd.ops) == "table" then count = #cmd.ops end
    return string.format("seq=%s token=%s ops=%d", tostring(cmd.seq), tostring(cmd.token), count)
end

--- A command is only executed once. The last executed (token, seq) pair is
--- mirrored into state.json, so neither a savegame reload nor a game restart
--- can replay a command.
local function isFresh(cmd)
    local token = tostring(cmd.token or "")
    local seq = tonumber(cmd.seq) or 0
    if token ~= ET.ackToken then return true, token, seq end
    return seq > ET.ackSeq, token, seq
end

local function poll()
    if not ET.inSession then return end

    -- The extender version is queried lazily: Osiris access is restricted
    -- during the SessionLoaded callback.
    if ET.extenderVersion == nil then
        local okVersion, version = pcall(Osi.NRD_GetVersion)
        if okVersion and version ~= nil then ET.extenderVersion = version end
    end

    local host = ET.try(Osi.CharacterGetHostCharacter)
    if type(host) ~= "string" or #host == 0 then return end

    local cmd = ET.readCommand()
    if cmd then
        local fresh, token, seq = isFresh(cmd)
        if fresh then
            ET.log("command received: %s", describeCommand(cmd))
            ET.token, ET.seq = token, seq
            ET.saveAck(token, seq)
            ET.lastErrors = ET.runOps(cmd)
            ET.pushState()
            return
        end
    end

    local now = ET.now()
    if now - ET.lastHeartbeat >= ET.HEARTBEAT_MS then
        ET.lastHeartbeat = now
        ET.pushState()
    end
end

function ET.pushState()
    local ok, state = pcall(ET.snapshot)
    if not ok then
        ET.log("snapshot failed: %s", tostring(state))
        state = {
            version = ET.VERSION,
            bridge = true,
            inGame = false,
            ok = false,
            seq = ET.seq,
            token = ET.token,
            errors = { tostring(state) },
            party = {},
        }
    end

    if ET.writeState(state) then
        ET.flushLog()
    end
end

local function onSessionLoading()
    ET.inSession = false
    ET.log("session loading")
end

local function describeGameVersion()
    local ok, version = pcall(Ext.Utils.GameVersion)
    if not ok or version == nil then return nil end

    local okFormat, text = pcall(function()
        return string.format("%d.%d.%d.%d", version.Major, version.Minor, version.Revision, version.Build)
    end)

    return okFormat and text or tostring(version)
end

--- Starts polling. Called both from the SessionLoaded event and, in console
--- mode, right after the app injected the code (which can happen while a
--- session is already running).
function ET.activate()
    if ET.inSession then return end
    ET.inSession = true
    ET.loadAck()

    ET.gameVersion = describeGameVersion()

    local okSelf, info = pcall(ET.selfTest)
    ET.log("session active (game %s, bridge write: %s %s)",
        tostring(ET.gameVersion), tostring(okSelf), tostring(info))

    ET.pushState()
end

local function onSessionLoaded()
    ET.activate()
end

local function onResetCompleted()
    ET.log("lua state reset")
end

Ext.RegisterListener("SessionLoading", onSessionLoading)
Ext.RegisterListener("SessionLoaded", onSessionLoaded)
Ext.RegisterListener("ResetCompleted", onResetCompleted)

Ext.RegisterListener("Tick", function()
    local now = ET.now()
    if now - ET.lastPoll < ET.POLL_INTERVAL_MS then return end
    ET.lastPoll = now

    local ok, err = pcall(poll)
    if not ok then
        ET.log("poll error: %s", tostring(err))
        ET.flushLog()
    end
end)

ET.log("EoC Trainer %s bootstrap complete", ET.VERSION)
ET.RUNNING = true
ET.flushLog()

-- Console injections happen while a session is already running, in which case
-- the SessionLoaded event will never fire again for this Lua state.
if type(ET.try(Osi.CharacterGetHostCharacter)) == "string" then
    ET.activate()
end
