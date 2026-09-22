--- EoC Trainer - file bridge between the running game and the desktop app.
---
--- The Script Extender sandbox has no `io` / `os` library, but it does expose
--- `Ext.IO.LoadFile` / `Ext.IO.SaveFile`, which are rooted at
---   <Documents>\Larian Studios\Divinity Original Sin 2 Definitive Edition\Osiris Data\
--- so both sides talk through JSON documents in `Osiris Data\EocTrainer`.
---
---   command.json  app  -> game   {"token":"...","seq":1,"ops":[...]}
---   state.json    game -> app    {"token":"...","seq":1,"party":[...]}
---   log.txt       game -> app    human readable diagnostics

EocTrainer = EocTrainer or {}
local ET = EocTrainer

ET.VERSION = "0.1.0"

ET.DIR = "EocTrainer/"
ET.CMD_FILE = ET.DIR .. "command.json"
ET.STATE_FILE = ET.DIR .. "state.json"
ET.LOG_FILE = ET.DIR .. "log.txt"

--- Commands are only re-read this often (milliseconds).
ET.POLL_INTERVAL_MS = 120
--- A state snapshot is pushed to the app at least this often.
ET.HEARTBEAT_MS = 1000

local MAX_LOG_LINES = 300
local logLines = {}
local logDirty = false
local logOverflow = 0

--- pcall wrapper that returns the first result, or nil plus an error message.
function ET.try(f, ...)
    local ok, a, b, c = pcall(f, ...)
    if not ok then return nil, tostring(a) end
    return a, b, c
end

--- Safe printf; never throws, even when the format string contains stray '%'.
function ET.log(fmt, ...)
    local msg
    local n = select('#', ...)
    if n == 0 then
        msg = tostring(fmt)
    else
        local ok, formatted = pcall(string.format, tostring(fmt), ...)
        msg = ok and formatted or (tostring(fmt) .. " | " .. table.concat({ ... }, " "))
    end

    local stamp = 0
    local okTime, t = pcall(Ext.MonotonicTime)
    if okTime and type(t) == "number" then
        stamp = math.floor(t)
    end

    logLines[#logLines + 1] = string.format("[%d] %s", stamp, msg)
    if #logLines > MAX_LOG_LINES then
        local excess = #logLines - MAX_LOG_LINES
        for _ = 1, excess do
            table.remove(logLines, 1)
        end
        logOverflow = logOverflow + excess
    end
    logDirty = true
end

--- Remembers which command was executed last. The acknowledgement lives in
--- state.json (not just in memory), so a savegame reload or a game restart can
--- never execute the same command twice.
function ET.loadAck()
    local state = ET.readState()
    if state then
        ET.ackToken = tostring(state.token or "")
        ET.ackSeq = tonumber(state.seq) or 0
    end
end

function ET.saveAck(token, seq)
    ET.ackToken = token
    ET.ackSeq = seq
end

--- Reads the previously written state document, if any.
function ET.readState()
    local okFile, exists = pcall(Ext.IO.IsFile, ET.STATE_FILE)
    if not okFile or not exists then return nil end

    local okRead, text = pcall(Ext.IO.LoadFile, ET.STATE_FILE)
    if not okRead or type(text) ~= "string" or #text == 0 then return nil end

    local okParse, state = pcall(Ext.Json.Parse, text)
    if not okParse or type(state) ~= "table" then return nil end
    return state
end

--- Verifies that the bridge directory can be reached at all. Returns the path
--- that was probed, so the app can tell the user where to look.
function ET.selfTest()
    local ok, err = pcall(function()
        Ext.IO.SaveFile(ET.DIR .. "hello.txt", "EocTrainer " .. ET.VERSION)
    end)
    return ok, tostring(err or "")
end

--- Writes the diagnostic log. Only touches the disk when something changed.
function ET.flushLog()
    if not logDirty then return end
    logDirty = false

    local header = string.format(
        "--- EoC Trainer %s | game %s | extender %s | %d line(s) dropped ---\n",
        ET.VERSION, tostring(ET.gameVersion or "?"), tostring(ET.extenderVersion or "?"), logOverflow)

    local ok = pcall(Ext.IO.SaveFile, ET.LOG_FILE, header .. table.concat(logLines, "\n") .. "\n")
    if ok then
        logOverflow = 0
    end
end

--- Atomically(ish) reads the pending command document.
--- Returns the decoded table or nil.
function ET.readCommand()
    local okFile, exists = pcall(Ext.IO.IsFile, ET.CMD_FILE)
    if not okFile or not exists then return nil end

    local okRead, text = pcall(Ext.IO.LoadFile, ET.CMD_FILE)
    if not okRead or type(text) ~= "string" or #text == 0 then return nil end

    local okParse, cmd = pcall(Ext.Json.Parse, text)
    if not okParse or type(cmd) ~= "table" then
        ET.log("command.json could not be parsed: %s", tostring(cmd))
        return nil
    end

    return cmd
end

function ET.writeState(state)
    local ok, json = pcall(Ext.Json.Stringify, state)
    if not ok then
        ET.log("state could not be serialised: %s", tostring(json))
        return false
    end

    local okSave = pcall(Ext.IO.SaveFile, ET.STATE_FILE, json)
    if not okSave then
        ET.log("state.json could not be written")
        return false
    end

    return true
end
